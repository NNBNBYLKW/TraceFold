import { http, HttpResponse } from "msw";

import { apiClient } from "../src/shared/api/client";
import { server } from "./server";


test("preserves stable API errors", async () => {
  server.use(
    http.post("*/api/inbox/items/1/ingest", () =>
      HttpResponse.json(
        {
          error: {
            code: "SOURCE_CHANGED",
            message: "源文件已发生变化，请重新扫描。",
            details: { item_id: "1" },
          },
        },
        { status: 409 },
      ),
    ),
  );

  await expect(apiClient.post("/inbox/items/1/ingest")).rejects.toMatchObject({
    code: "SOURCE_CHANGED",
    status: 409,
    details: { item_id: "1" },
  });
});


test("sends a caller-owned idempotency key", async () => {
  let receivedKey: string | null = null;
  server.use(
    http.post("*/api/inbox/items/1/ingest", ({ request }) => {
      receivedKey = request.headers.get("Idempotency-Key");
      return HttpResponse.json({ kind: "completed" });
    }),
  );

  await apiClient.post("/inbox/items/1/ingest", undefined, {
    idempotencyKey: "command-1",
  });

  expect(receivedKey).toBe("command-1");
});
