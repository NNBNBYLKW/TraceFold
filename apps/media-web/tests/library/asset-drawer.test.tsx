import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { AssetDrawer } from "../../src/library/AssetDrawer";
import type { MetadataPatch } from "../../src/shared/api/types";
import { server } from "../server";
import { authors, series, tags } from "../inbox/fixtures";
import { renderWithAppProviders } from "../inbox/test-utils";
import { libraryAsset } from "./fixtures";


function installTaxonomyHandlers() {
  server.use(
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
}

test("edits only display metadata while physical names remain read only", async () => {
  installTaxonomyHandlers();
  let submitted: MetadataPatch | null = null;
  server.use(
    http.patch("*/api/library/assets/asset-1/metadata", async ({ request }) => {
      submitted = (await request.json()) as MetadataPatch;
      return HttpResponse.json({ ...libraryAsset, ...submitted });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<AssetDrawer asset={libraryAsset} onClose={() => undefined} />);

  expect(screen.getByLabelText("实际文件名")).toHaveAttribute("readonly");
  expect(screen.getByLabelText("库内相对位置")).toHaveAttribute("readonly");
  await user.clear(screen.getByLabelText("显示名称"));
  await user.type(screen.getByLabelText("显示名称"), "西湖夜景");
  await user.click(screen.getByRole("button", { name: "保存资料" }));

  await waitFor(() => expect(submitted).toEqual({
    display_name: "西湖夜景",
    author_ids: ["author-1"],
    series_id: "series-1",
    tag_ids: ["tag-1"],
  }));
});

test("reveals an asset through its opaque API id without sending a path", async () => {
  installTaxonomyHandlers();
  let requestBody = "not-called";
  server.use(
    http.post("*/api/library/assets/asset-1/reveal", async ({ request }) => {
      requestBody = await request.text();
      return HttpResponse.json({ asset_id: "asset-1", revealed: true });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<AssetDrawer asset={libraryAsset} onClose={() => undefined} />);

  await user.click(screen.getByRole("button", { name: "打开所在目录" }));

  await waitFor(() => expect(requestBody).toBe(""));
});
