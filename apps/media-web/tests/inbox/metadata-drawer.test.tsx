import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { MetadataDrawer } from "../../src/inbox/MetadataDrawer";
import type { MetadataPatch } from "../../src/shared/api/types";
import { server } from "../server";
import { authors, processingItem, readyItem, series, tags } from "./fixtures";
import { renderWithAppProviders } from "./test-utils";


function useTaxonomyHandlers() {
  server.use(
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
}

test("edits display metadata but keeps actual filename read only", async () => {
  useTaxonomyHandlers();
  let submitted: MetadataPatch | null = null;
  server.use(
    http.patch("*/api/inbox/items/item-1/metadata", async ({ request }) => {
      submitted = (await request.json()) as MetadataPatch;
      return HttpResponse.json({ ...readyItem, ...submitted });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<MetadataDrawer item={readyItem} onClose={() => undefined} />);

  expect(screen.getByLabelText("实际文件名")).toHaveAttribute("readonly");
  await user.clear(screen.getByLabelText("显示名称"));
  await user.type(screen.getByLabelText("显示名称"), "杭州夜游");
  await user.selectOptions(await screen.findByLabelText("系列"), "series-1");
  await user.click(screen.getByRole("button", { name: "保存资料" }));

  await waitFor(() => {
    expect(submitted).toEqual({
      display_name: "杭州夜游",
      author_ids: readyItem.author_ids,
      series_id: "series-1",
      tag_ids: readyItem.tag_ids,
    });
  });
});


test("does not show ingest when the server disallows it", async () => {
  useTaxonomyHandlers();
  renderWithAppProviders(
    <MetadataDrawer item={processingItem} onClose={() => undefined} />,
  );

  expect(await screen.findByText("城市漫游.mp4")).toBeVisible();
  expect(screen.getByLabelText("显示名称")).toBeDisabled();
  expect(screen.getByLabelText("系列")).toBeDisabled();
  expect(screen.queryByRole("button", { name: "正式入库" })).not.toBeInTheDocument();
});


test("creates and selects a new series without leaving the drawer", async () => {
  useTaxonomyHandlers();
  let submitted: MetadataPatch | null = null;
  const availableSeries = [...series];
  server.use(
    http.get("*/api/series", () => HttpResponse.json(availableSeries)),
    http.post("*/api/series", async ({ request }) => {
      const body = await request.json() as { name: string };
      const created = { id: "series-new", display_name: body.name };
      availableSeries.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.patch("*/api/inbox/items/item-1/metadata", async ({ request }) => {
      submitted = (await request.json()) as MetadataPatch;
      return HttpResponse.json({ ...readyItem, ...submitted });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<MetadataDrawer item={readyItem} onClose={() => undefined} />);

  await user.type(await screen.findByLabelText("搜索或创建系列"), "周末短片");
  await user.click(screen.getByRole("button", { name: "创建“周末短片”" }));
  await waitFor(() => expect(screen.getByLabelText("系列")).toHaveValue("series-new"));
  await user.click(screen.getByRole("button", { name: "保存资料" }));

  await waitFor(() => expect(submitted).toMatchObject({ series_id: "series-new" }));
});


test("passes a server conflict to the dialog flow without guessing a choice", async () => {
  useTaxonomyHandlers();
  let idempotencyKey: string | null = null;
  let conflictId: string | null = null;
  server.use(
    http.post("*/api/inbox/items/item-1/ingest", ({ request }) => {
      idempotencyKey = request.headers.get("Idempotency-Key");
      return HttpResponse.json({
        kind: "conflict",
        operation_id: "conflict-1",
        asset_id: null,
        conflict_id: "conflict-1",
        cleanup_warning: null,
      });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(
    <MetadataDrawer
      item={readyItem}
      onClose={() => undefined}
      onConflict={(id) => { conflictId = id; }}
    />,
  );

  await user.click(screen.getByRole("button", { name: "正式入库" }));

  await waitFor(() => expect(conflictId).toBe("conflict-1"));
  expect(idempotencyKey).toMatch(/[0-9a-f-]{36}/i);
});
