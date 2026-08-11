import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { BulkMetadataBar } from "../../src/inbox/BulkMetadataBar";
import type { BulkMetadataPatch } from "../../src/shared/api/types";
import { server } from "../server";
import {
  authors,
  processingItem,
  readyItem,
  secondReadyItem,
  series,
  tags,
} from "./fixtures";
import { renderWithAppProviders } from "./test-utils";


function useTaxonomyHandlers() {
  server.use(
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
}

test("appends authors and tags and explicitly replaces series", async () => {
  useTaxonomyHandlers();
  let submitted: BulkMetadataPatch | null = null;
  server.use(
    http.patch("*/api/inbox/items/bulk-metadata", async ({ request }) => {
      submitted = (await request.json()) as BulkMetadataPatch;
      return HttpResponse.json({ updated_count: 2 });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(
    <BulkMetadataBar items={[readyItem, secondReadyItem]} onApplied={() => undefined} />,
  );

  await user.click(await screen.findByRole("checkbox", { name: "林同学" }));
  await user.click(screen.getByRole("checkbox", { name: "夜景" }));
  await user.selectOptions(screen.getByLabelText("批量系列"), "series-1");
  expect(screen.getByText("系列将替换所选文件的现有系列")).toBeVisible();
  await user.click(screen.getByRole("button", { name: "应用到 2 项" }));

  await waitFor(() => {
    expect(submitted).toEqual({
      item_ids: ["item-1", "item-4"],
      append_author_ids: ["author-1"],
      append_tag_ids: ["tag-1"],
      replace_series: true,
      replace_series_id: "series-1",
    });
  });
});


test("disables bulk editing when any selected item is not editable", async () => {
  useTaxonomyHandlers();
  renderWithAppProviders(
    <BulkMetadataBar items={[readyItem, processingItem]} onApplied={() => undefined} />,
  );

  expect(screen.getByRole("button", { name: "应用到 2 项" })).toBeDisabled();
  expect(screen.getByText("所选文件中包含当前不可编辑的项目")).toBeVisible();
});
