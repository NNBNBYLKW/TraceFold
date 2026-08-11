import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { apiClient } from "../api/client";
import type { MediaEventRead } from "../api/types";


export type MediaEventConnection = "connecting" | "connected" | "disconnected";

const eventTypes = [
  "inbox.updated",
  "inbox.scanned",
  "processing.updated",
  "library.updated",
  "conflict.resolved",
  "system.ready",
  "system.stopping",
] as const;

export function useMediaEvents(): MediaEventConnection {
  const queryClient = useQueryClient();
  const [connection, setConnection] = useState<MediaEventConnection>("connecting");

  useEffect(() => {
    if (typeof EventSource === "undefined") {
      setConnection("disconnected");
      return undefined;
    }
    const source = new EventSource(apiClient.url("/events"));
    source.onopen = () => setConnection("connected");
    source.onerror = () => setConnection("disconnected");

    const handleEvent = (rawEvent: Event) => {
      if (!(rawEvent instanceof MessageEvent) || typeof rawEvent.data !== "string") return;
      let event: MediaEventRead;
      try {
        event = JSON.parse(rawEvent.data) as MediaEventRead;
      } catch {
        return;
      }
      if (event.type === "library.updated") {
        void queryClient.invalidateQueries({ queryKey: ["library"] });
        return;
      }
      if (
        event.type === "inbox.updated" ||
        event.type === "inbox.scanned" ||
        event.type === "processing.updated"
      ) {
        void queryClient.invalidateQueries({ queryKey: ["inbox"] });
      }
      if (event.type === "conflict.resolved") {
        void queryClient.invalidateQueries({ queryKey: ["inbox"] });
        void queryClient.invalidateQueries({ queryKey: ["library"] });
      }
      if (
        event.type === "inbox.scanned" ||
        event.type === "processing.updated" ||
        event.type.startsWith("system.")
      ) {
        void queryClient.invalidateQueries({ queryKey: ["system", "status"] });
      }
    };

    eventTypes.forEach((type) => source.addEventListener(type, handleEvent));
    return () => {
      eventTypes.forEach((type) => source.removeEventListener(type, handleEvent));
      source.close();
    };
  }, [queryClient]);

  return connection;
}
