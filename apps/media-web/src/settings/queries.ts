import { useQuery } from "@tanstack/react-query";

import { apiClient } from "../shared/api/client";
import type { RuntimeSettingsRead, SystemStatusRead } from "../shared/api/types";


export const settingsKeys = {
  all: ["settings"] as const,
  status: ["system", "status"] as const,
};

export function useRuntimeSettings() {
  return useQuery({
    queryKey: settingsKeys.all,
    queryFn: () => apiClient.get<RuntimeSettingsRead>("/settings"),
  });
}

export function useSystemStatus() {
  return useQuery({
    queryKey: settingsKeys.status,
    queryFn: () => apiClient.get<SystemStatusRead>("/system/status"),
    staleTime: 5_000,
  });
}
