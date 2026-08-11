import { screen } from "@testing-library/react";

import { App } from "../src/app/App";
import { renderWithAppProviders } from "./inbox/test-utils";


test("shows only the three product destinations", () => {
  renderWithAppProviders(<App />);

  expect(screen.getByRole("link", { name: /待入库/ })).toBeVisible();
  expect(screen.getByRole("link", { name: /文件库/ })).toBeVisible();
  expect(screen.getByRole("link", { name: /设置/ })).toBeVisible();
  expect(screen.queryByText(/Workbench|Capture|Knowledge/)).not.toBeInTheDocument();
});
