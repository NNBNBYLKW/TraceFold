import { defineConfig } from "@playwright/test";


export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: "http://127.0.0.1:3101",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: [
    {
      command: "python ../media-api/scripts/run_e2e_api.py",
      url: "http://127.0.0.1:8101/health",
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        TRACEFOLD_MEDIA_WEB_ORIGIN: "http://127.0.0.1:3101",
      },
    },
    {
      command: "npm run dev -- --port 3101",
      url: "http://127.0.0.1:3101",
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        VITE_API_BASE_URL: "http://127.0.0.1:8101/api",
      },
    },
  ],
});
