import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

export const defaultSystemStatus = {
  directories_writable: true,
  ffmpeg_available: true,
  ffprobe_available: true,
  last_scan_at: "2026-08-12T08:00:00+08:00",
  queued_jobs: 0,
  recoverable_operations: 0,
  recovery_in_progress: false,
  ingest_enabled: true,
};

export const server = setupServer(
  http.get("*/api/system/status", () => HttpResponse.json(defaultSystemStatus)),
);
