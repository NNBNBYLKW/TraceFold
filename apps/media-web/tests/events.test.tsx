import { act, screen, waitFor } from "@testing-library/react";
import { vi } from "vitest";

import { App } from "../src/app/App";
import { renderWithAppProviders } from "./inbox/test-utils";


class FakeEventSource {
  static instances: FakeEventSource[] = [];

  readonly url: string;
  onopen: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  private listeners = new Map<string, Set<(event: MessageEvent) => void>>();

  constructor(url: string | URL) {
    this.url = String(url);
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    const callback = typeof listener === "function"
      ? listener as (event: MessageEvent) => void
      : (event: MessageEvent) => listener.handleEvent(event);
    const existing = this.listeners.get(type) ?? new Set();
    existing.add(callback);
    this.listeners.set(type, existing);
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject) {
    if (typeof listener === "function") {
      this.listeners.get(type)?.delete(listener as (event: MessageEvent) => void);
    }
  }

  close() {
    this.closed = true;
  }

  emit(type: string, payload: object) {
    const event = new MessageEvent(type, { data: JSON.stringify(payload) });
    this.listeners.get(type)?.forEach((listener) => listener(event));
  }

  open() {
    this.onopen?.(new Event("open"));
  }

  disconnect() {
    this.onerror?.(new Event("error"));
  }
}

beforeEach(() => {
  FakeEventSource.instances = [];
  vi.stubGlobal("EventSource", FakeEventSource);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

test("invalidates affected query families after named SSE events", async () => {
  const { queryClient, unmount } = renderWithAppProviders(<App />);
  queryClient.setQueryData(["inbox"], []);
  queryClient.setQueryData(["inbox", "item", "item-1"], { id: "item-1" });
  queryClient.setQueryData(["library"], []);
  queryClient.setQueryData(["library", "item", "asset-1"], { id: "asset-1" });
  const source = FakeEventSource.instances[0];
  expect(source).toBeDefined();
  expect(source?.url).toBe("http://localhost:3000/api/events");

  act(() => source?.open());
  expect(await screen.findByText("实时更新已连接")).toBeVisible();
  act(() => source?.emit("inbox.updated", {
    type: "inbox.updated",
    entity_id: "item-1",
    status: "ready_to_tag",
    timestamp: "2026-08-12T08:00:00Z",
  }));
  await waitFor(() => {
    expect(queryClient.getQueryState(["inbox"])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(["inbox", "item", "item-1"])?.isInvalidated).toBe(true);
  });

  act(() => source?.emit("library.updated", {
    type: "library.updated",
    entity_id: "asset-1",
    status: "metadata_updated",
    timestamp: "2026-08-12T08:01:00Z",
  }));
  await waitFor(() => {
    expect(queryClient.getQueryState(["library"])?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(["library", "item", "asset-1"])?.isInvalidated).toBe(true);
  });

  unmount();
  expect(source?.closed).toBe(true);
});

test("shows a non-blocking status while browser reconnection is pending", async () => {
  renderWithAppProviders(<App />);
  const source = FakeEventSource.instances[0];

  act(() => source?.disconnect());

  expect(await screen.findByText("实时更新已断开，可继续手动刷新")).toBeVisible();
  expect(screen.getByRole("navigation", { name: "产品导航" })).toBeVisible();
});
