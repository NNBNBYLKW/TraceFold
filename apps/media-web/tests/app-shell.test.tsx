import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { App } from "../src/app/App";


test("shows only the three product destinations", () => {
  render(
    <MemoryRouter initialEntries={["/inbox"]}>
      <App />
    </MemoryRouter>,
  );

  expect(screen.getByRole("link", { name: /待入库/ })).toBeVisible();
  expect(screen.getByRole("link", { name: /文件库/ })).toBeVisible();
  expect(screen.getByRole("link", { name: /设置/ })).toBeVisible();
  expect(screen.queryByText(/Workbench|Capture|Knowledge/)).not.toBeInTheDocument();
});
