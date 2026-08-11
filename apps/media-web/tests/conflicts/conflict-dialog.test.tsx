import { useRef, useState } from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";

import { ConflictDialog } from "../../src/conflicts/ConflictDialog";
import type {
  ConflictRead,
  ConflictResolution,
  ConflictResolutionRead,
} from "../../src/shared/api/types";
import { server } from "../server";
import { authors, readyItem, series, tags } from "../inbox/fixtures";
import { renderWithAppProviders } from "../inbox/test-utils";


const conflict: ConflictRead = {
  operation_id: "conflict-1",
  conflict_kinds: ["name", "content"],
  incoming_item: {
    ...readyItem,
    actual_filename: "本次下载.mp4",
    sources: [
      {
        id: "source-1",
        filename: "本次下载.mp4",
        extension: ".mp4",
        size: 12_500_000,
        is_primary: true,
      },
    ],
  },
  existing_asset: {
    id: "asset-1",
    media_type: "video",
    actual_filename: "文件库原片.mp4",
    display_name: "杭州旧片",
    author_ids: ["author-2"],
    series_id: "series-1",
    tag_ids: ["tag-2"],
    size: 11_800_000,
    ingested_at: "2026-08-10T08:30:00+08:00",
  },
  incoming_preview_url: "/api/inbox/items/item-1/preview",
  existing_preview_url: "/api/library/assets/asset-1/preview",
  source_hash: "a".repeat(64),
  requested_actual_filename: "文件库原片.mp4",
};

function installReadHandlers() {
  server.use(
    http.get("*/api/conflicts/conflict-1", () => HttpResponse.json(conflict)),
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
}

interface HarnessProps {
  onResolved?: (resolution: ConflictResolution) => void;
  onExpired?: () => void;
}

function ConflictHarness({ onResolved, onExpired }: HarnessProps) {
  const [open, setOpen] = useState(true);
  const triggerRef = useRef<HTMLButtonElement>(null);
  return (
    <>
      <button ref={triggerRef} type="button">正式入库</button>
      {open && (
        <ConflictDialog
          conflictId="conflict-1"
          returnFocusRef={triggerRef}
          onClose={() => setOpen(false)}
          onExpired={() => {
            onExpired?.();
            setOpen(false);
          }}
          onResolved={(resolution) => {
            onResolved?.(resolution);
            setOpen(false);
          }}
        />
      )}
    </>
  );
}

test("shows both files and registered metadata before offering choices", async () => {
  installReadHandlers();
  renderWithAppProviders(<ConflictHarness />);

  expect(await screen.findByRole("img", { name: "文件库中的文件" })).toBeVisible();
  expect(screen.getByRole("img", { name: "本次待入库文件" })).toBeVisible();
  expect(screen.getByText("文件库原片.mp4")).toBeVisible();
  expect(screen.getByText("本次下载.mp4")).toBeVisible();
  expect(screen.getByText("陈老师")).toBeVisible();
  expect(screen.getByText("城市漫游")).toBeVisible();
  expect(screen.getByText("文件名相同")).toBeVisible();
  expect(screen.getByText("文件内容重复")).toBeVisible();
  expect(screen.getByRole("button", { name: "跳过" })).toBeVisible();
  expect(screen.getByRole("button", { name: "两者都保留" })).toBeVisible();
  expect(screen.getByRole("button", { name: "替换原文件" })).toBeVisible();
});

test.each([
  ["跳过", "skip"],
  ["两者都保留", "keep_both"],
  ["替换原文件", "replace"],
] as const)("submits %s as %s with a command key", async (label, resolution) => {
  installReadHandlers();
  let submitted: ConflictResolution | null = null;
  let idempotencyKey: string | null = null;
  server.use(
    http.post("*/api/conflicts/conflict-1/resolve", async ({ request }) => {
      const body = await request.json() as { resolution: ConflictResolution };
      submitted = body.resolution;
      idempotencyKey = request.headers.get("Idempotency-Key");
      return HttpResponse.json({
        operation_id: "conflict-1",
        resolution: body.resolution,
        kind: body.resolution === "skip" ? "skipped" : "completed",
        asset_id: body.resolution === "skip" ? null : "asset-2",
        cleanup_warning: null,
      } satisfies ConflictResolutionRead);
    }),
  );
  let resolved: ConflictResolution | null = null;
  const user = userEvent.setup();
  renderWithAppProviders(<ConflictHarness onResolved={(value) => { resolved = value; }} />);

  await user.click(await screen.findByRole("button", { name: label }));
  if (resolution === "replace") {
    expect(screen.getByText("现有文件及其资料将被本次文件替换。此操作无法撤销。")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "确认替换" }));
  }

  await waitFor(() => expect(submitted).toBe(resolution));
  expect(resolved).toBe(resolution);
  expect(idempotencyKey).toMatch(/[0-9a-f-]{36}/i);
});

test("traps keyboard focus, closes on Escape, and restores trigger focus", async () => {
  installReadHandlers();
  const user = userEvent.setup();
  renderWithAppProviders(<ConflictHarness />);

  const close = await screen.findByRole("button", { name: "关闭冲突对话框" });
  await waitFor(() => expect(close).toHaveFocus());
  await user.keyboard("{Shift>}{Tab}{/Shift}");
  expect(screen.getByRole("button", { name: "替换原文件" })).toHaveFocus();
  await user.tab();
  expect(close).toHaveFocus();
  await user.keyboard("{Escape}");

  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "正式入库" })).toHaveFocus();
});

test("does not close with Escape while a resolution is being submitted", async () => {
  installReadHandlers();
  server.use(
    http.post("*/api/conflicts/conflict-1/resolve", async () => {
      await delay(80);
      return HttpResponse.json({
        operation_id: "conflict-1",
        resolution: "keep_both",
        kind: "completed",
        asset_id: "asset-2",
        cleanup_warning: null,
      } satisfies ConflictResolutionRead);
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<ConflictHarness />);

  await user.click(await screen.findByRole("button", { name: "两者都保留" }));
  await user.keyboard("{Escape}");
  expect(screen.getByRole("dialog")).toBeVisible();
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(screen.getByRole("button", { name: "正式入库" })).toHaveFocus();
});

test("reports an expired choice so the parent can refetch the inbox item", async () => {
  installReadHandlers();
  server.use(
    http.post("*/api/conflicts/conflict-1/resolve", () =>
      HttpResponse.json(
        {
          error: {
            code: "CONFLICT_CHOICE_EXPIRED",
            message: "源文件已变化，请重新选择。",
            details: { operation_id: "conflict-1" },
          },
        },
        { status: 409 },
      ),
    ),
  );
  let expired = false;
  const user = userEvent.setup();
  renderWithAppProviders(<ConflictHarness onExpired={() => { expired = true; }} />);

  await user.click(await screen.findByRole("button", { name: "跳过" }));

  await waitFor(() => expect(expired).toBe(true));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});
