import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { InboxPage } from "../../src/inbox/InboxPage";
import { server } from "../server";
import { failedItem, processingItem, readyItem } from "./fixtures";
import { renderWithAppProviders } from "./test-utils";


test("refreshes before rendering server-owned statuses", async () => {
  let scanned = false;
  server.use(
    http.get("*/api/inbox/items", () =>
      HttpResponse.json(scanned ? [readyItem, processingItem, failedItem] : []),
    ),
    http.post("*/api/inbox/scan", () => {
      scanned = true;
      return HttpResponse.json({
        observed_candidates: 3,
        stable_candidates: 3,
        created_items: 3,
      });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<InboxPage />);

  expect(await screen.findByText("待入库目录中还没有可处理文件")).toBeVisible();
  await user.click(screen.getByRole("button", { name: "手动刷新" }));

  expect(await screen.findByText("旅行记录_杭州_01.mp4")).toBeVisible();
  expect(screen.getByText("待标注", { selector: ".status-badge" })).toBeVisible();
  expect(screen.getByText("合并中 68%")).toBeVisible();
  expect(screen.getByText("处理失败")).toBeVisible();
});


test("filters cards without changing server-owned state", async () => {
  let requestCount = 0;
  server.use(
    http.get("*/api/inbox/items", () => {
      requestCount += 1;
      return HttpResponse.json([readyItem, processingItem, failedItem]);
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<InboxPage />);

  await screen.findByText("旅行记录_杭州_01.mp4");
  await user.type(screen.getByRole("searchbox"), "城市");

  expect(screen.getByText("城市漫游.mp4")).toBeVisible();
  expect(screen.queryByText("旅行记录_杭州_01.mp4")).not.toBeInTheDocument();
  expect(screen.queryByText("损坏片段.mp4")).not.toBeInTheDocument();
  expect(requestCount).toBe(1);
});
