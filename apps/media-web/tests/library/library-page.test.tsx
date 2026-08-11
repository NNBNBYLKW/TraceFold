import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { LibraryPage } from "../../src/library/LibraryPage";
import { server } from "../server";
import { authors, series, tags } from "../inbox/fixtures";
import { renderWithAppProviders } from "../inbox/test-utils";
import { assetWithoutDisplayName, libraryAsset } from "./fixtures";


function installTaxonomyHandlers() {
  server.use(
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
}

test("combines search and taxonomy filters in the library request", async () => {
  installTaxonomyHandlers();
  let lastQuery = new URLSearchParams();
  server.use(
    http.get("*/api/library/assets", ({ request }) => {
      lastQuery = new URL(request.url).searchParams;
      return HttpResponse.json([]);
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<LibraryPage />);

  await user.type(screen.getByRole("searchbox"), "杭州");
  await screen.findByRole("option", { name: "林同学" });
  await user.selectOptions(screen.getByLabelText("作者"), "author-1");
  await user.selectOptions(screen.getByLabelText("系列"), "series-1");
  await user.selectOptions(screen.getByLabelText("标签"), "tag-1");
  await user.selectOptions(screen.getByLabelText("类型"), "video");

  await waitFor(() => expect(Object.fromEntries(lastQuery)).toEqual({
    search: "杭州",
    author_id: "author-1",
    series_id: "series-1",
    tag_id: "tag-1",
    media_type: "video",
  }));
});

test("falls back to the actual filename when display name is empty", async () => {
  installTaxonomyHandlers();
  server.use(
    http.get("*/api/library/assets", () => HttpResponse.json([assetWithoutDisplayName])),
  );
  renderWithAppProviders(<LibraryPage />);

  expect(await screen.findByText(assetWithoutDisplayName.actual_filename)).toBeVisible();
});

test("opens an asset from the gallery without exposing a filesystem command", async () => {
  installTaxonomyHandlers();
  server.use(
    http.get("*/api/library/assets", () => HttpResponse.json([libraryAsset])),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<LibraryPage />);

  await user.click(await screen.findByRole("button", { name: "打开 hangzhou-night.mp4" }));

  expect(screen.getByLabelText("正式文件资料")).toBeVisible();
  expect(screen.getByLabelText("库内相对位置")).toHaveValue("hangzhou-night.mp4");
});
