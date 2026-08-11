import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { InboxPage } from "../../src/inbox/InboxPage";
import type { ConflictRead, ConflictResolutionRead } from "../../src/shared/api/types";
import { server } from "../server";
import { authors, readyItem, series, tags } from "../inbox/fixtures";
import { renderWithAppProviders } from "../inbox/test-utils";


const conflict: ConflictRead = {
  operation_id: "conflict-1",
  conflict_kinds: ["name"],
  incoming_item: readyItem,
  existing_asset: {
    id: "asset-1",
    media_type: "video",
    actual_filename: readyItem.actual_filename,
    relative_path: readyItem.actual_filename,
    display_name: "正式库旧文件",
    author_ids: [],
    series_id: null,
    tag_ids: [],
    size: 10_000,
    ingested_at: "2026-08-10T08:30:00+08:00",
  },
  incoming_preview_url: readyItem.preview_url,
  existing_preview_url: "/api/library/assets/asset-1/preview",
  source_hash: "b".repeat(64),
  requested_actual_filename: readyItem.actual_filename,
};

function installConflictHandlers() {
  server.use(
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
    http.post("*/api/inbox/items/item-1/ingest", () => HttpResponse.json({
      kind: "conflict",
      operation_id: "conflict-1",
      asset_id: null,
      conflict_id: "conflict-1",
      cleanup_warning: null,
    })),
    http.get("*/api/conflicts/conflict-1", () => HttpResponse.json(conflict)),
  );
}

async function openConflict() {
  const user = userEvent.setup();
  await screen.findByText(readyItem.actual_filename);
  await user.click(screen.getByRole("checkbox", { name: `选择 ${readyItem.actual_filename}` }));
  await user.click(screen.getByRole("button", { name: `打开 ${readyItem.actual_filename}` }));
  await user.click(screen.getByRole("button", { name: "正式入库" }));
  await screen.findByRole("dialog");
  return user;
}

test("skip closes only the dialog and preserves the active selected inbox item", async () => {
  installConflictHandlers();
  server.use(
    http.get("*/api/inbox/items", () => HttpResponse.json([readyItem])),
    http.post("*/api/conflicts/conflict-1/resolve", () => HttpResponse.json({
      operation_id: "conflict-1",
      resolution: "skip",
      kind: "skipped",
      asset_id: null,
      cleanup_warning: null,
    } satisfies ConflictResolutionRead)),
  );
  renderWithAppProviders(<InboxPage />);
  const user = await openConflict();

  await user.click(screen.getByRole("button", { name: "跳过" }));

  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(screen.getByLabelText("文件资料")).toBeVisible();
  expect(screen.getByRole("checkbox", { name: `选择 ${readyItem.actual_filename}` })).toBeChecked();
});

test("Escape returns focus to the drawer's actual ingest button", async () => {
  installConflictHandlers();
  server.use(http.get("*/api/inbox/items", () => HttpResponse.json([readyItem])));
  renderWithAppProviders(<InboxPage />);
  const user = await openConflict();

  await user.keyboard("{Escape}");

  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(screen.getByRole("button", { name: "正式入库" })).toHaveFocus();
  expect(screen.getByRole("button", { name: "查看冲突" })).toBeVisible();
});

test("keep-both removes the completed inbox item, closes its drawer, and invalidates library", async () => {
  installConflictHandlers();
  let inboxItems = [readyItem];
  server.use(
    http.get("*/api/inbox/items", () => HttpResponse.json(inboxItems)),
    http.post("*/api/conflicts/conflict-1/resolve", () => {
      inboxItems = [];
      return HttpResponse.json({
        operation_id: "conflict-1",
        resolution: "keep_both",
        kind: "completed",
        asset_id: "asset-2",
        cleanup_warning: null,
      } satisfies ConflictResolutionRead);
    }),
  );
  const { queryClient } = renderWithAppProviders(<InboxPage />);
  queryClient.setQueryData(["library"], [{ id: "stale" }]);
  const user = await openConflict();

  await user.click(screen.getByRole("button", { name: "两者都保留" }));

  expect(await screen.findByText("待入库目录中还没有可处理文件")).toBeVisible();
  expect(screen.queryByLabelText("文件资料")).not.toBeInTheDocument();
  expect(queryClient.getQueryState(["library"])?.isInvalidated).toBe(true);
});

test("expired resolution refetches the inbox and explains what changed", async () => {
  installConflictHandlers();
  let inboxReads = 0;
  server.use(
    http.get("*/api/inbox/items", () => {
      inboxReads += 1;
      return HttpResponse.json([readyItem]);
    }),
    http.post("*/api/conflicts/conflict-1/resolve", () => HttpResponse.json(
      {
        error: {
          code: "CONFLICT_CHOICE_EXPIRED",
          message: "源文件已变化，请重新选择。",
          details: { operation_id: "conflict-1" },
        },
      },
      { status: 409 },
    )),
  );
  renderWithAppProviders(<InboxPage />);
  const user = await openConflict();

  await user.click(screen.getByRole("button", { name: "跳过" }));

  expect(await screen.findByText("源文件已变化，请重新选择。")).toBeVisible();
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  await waitFor(() => expect(inboxReads).toBeGreaterThan(1));
});
