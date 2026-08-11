import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";

import { MetadataDrawer } from "../../src/inbox/MetadataDrawer";
import { SettingsPage } from "../../src/settings/SettingsPage";
import type { RuntimeSettingsPatch } from "../../src/shared/api/types";
import { server, defaultSystemStatus } from "../server";
import { authors, readyItem, series, tags } from "../inbox/fixtures";
import { renderWithAppProviders } from "../inbox/test-utils";


const runtimeSettings = {
  inbox_dir: "T:\\Media\\Incoming",
  library_dir: "T:\\Media\\Library",
  work_dir: "T:\\Media\\Work",
  database_path: "T:\\Media\\Data\\media.sqlite3",
  scan_interval_seconds: 15,
  automatic_scan_enabled: false,
  processors: [
    { processor_name: "mp4_m4a_merge", enabled: true, auto_run: true },
  ],
};

function installSettingsHandlers() {
  server.use(
    http.get("*/api/settings", () => HttpResponse.json(runtimeSettings)),
    http.get("*/api/system/status", () => HttpResponse.json({
      ...defaultSystemStatus,
      queued_jobs: 2,
      recoverable_operations: 1,
    })),
  );
}

test("shows diagnostics and saves valid runtime paths and processor settings", async () => {
  installSettingsHandlers();
  let submitted: RuntimeSettingsPatch | null = null;
  server.use(
    http.patch("*/api/settings", async ({ request }) => {
      submitted = (await request.json()) as RuntimeSettingsPatch;
      return HttpResponse.json({ ...runtimeSettings, ...submitted });
    }),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<SettingsPage />);

  expect(await screen.findByText("FFmpeg 可用")).toBeVisible();
  expect(screen.getByText("队列中 2 项")).toBeVisible();
  await user.clear(screen.getByLabelText("待入库目录"));
  await user.type(screen.getByLabelText("待入库目录"), "T:\\Media\\Inbox");
  await user.click(screen.getByRole("button", { name: "保存设置" }));

  await waitFor(() => expect(submitted).toMatchObject({
    inbox_dir: "T:\\Media\\Inbox",
    library_dir: "T:\\Media\\Library",
    work_dir: "T:\\Media\\Work",
    scan_interval_seconds: 15,
    automatic_scan_enabled: false,
    processors: [
      { processor_name: "mp4_m4a_merge", enabled: true, auto_run: true },
    ],
  }));
  expect(submitted).not.toHaveProperty("database_path");
});

test("explains why path changes are temporarily blocked", async () => {
  installSettingsHandlers();
  server.use(
    http.patch("*/api/settings", () => HttpResponse.json(
      {
        error: {
          code: "SETTINGS_BUSY",
          message: "存在进行中的任务，暂时不能修改运行目录。",
          details: {},
        },
      },
      { status: 409 },
    )),
  );
  const user = userEvent.setup();
  renderWithAppProviders(<SettingsPage />);

  await screen.findByLabelText("待入库目录");
  await user.click(screen.getByRole("button", { name: "保存设置" }));

  expect(await screen.findByText(/存在正在处理或待恢复的文件/)).toBeVisible();
});

test("shows recovery state without blocking library-oriented settings", async () => {
  installSettingsHandlers();
  server.use(
    http.get("*/api/system/status", () => HttpResponse.json({
      ...defaultSystemStatus,
      recovery_in_progress: true,
      ingest_enabled: false,
      recoverable_operations: 3,
    })),
  );
  renderWithAppProviders(<SettingsPage />);

  expect(await screen.findByText("正在恢复未完成操作")).toBeVisible();
  expect(screen.getByText("可恢复操作 3 项")).toBeVisible();
  expect(screen.getByLabelText("正式文件目录")).toBeEnabled();
});

test("system recovery disables formal ingest but keeps metadata editable", async () => {
  server.use(
    http.get("*/api/system/status", () => HttpResponse.json({
      ...defaultSystemStatus,
      recovery_in_progress: true,
      ingest_enabled: false,
    })),
    http.get("*/api/authors", () => HttpResponse.json(authors)),
    http.get("*/api/series", () => HttpResponse.json(series)),
    http.get("*/api/tags", () => HttpResponse.json(tags)),
  );
  renderWithAppProviders(<MetadataDrawer item={readyItem} onClose={() => undefined} />);

  await waitFor(() => expect(screen.getByRole("button", { name: "正式入库" })).toBeDisabled());
  expect(screen.getByRole("button", { name: "保存资料" })).toBeEnabled();
  expect(screen.getByText("系统正在恢复，暂时不能正式入库。")).toBeVisible();
});
