import { useQuery } from "@tanstack/react-query";

import { apiClient } from "../shared/api/client";
import type { InboxItemRead, InboxStatus, MediaType } from "../shared/api/types";


export interface InboxFilters {
  search: string;
  status: InboxStatus | "all";
  mediaType: MediaType | "all";
}

export const inboxKeys = {
  all: ["inbox"] as const,
  list: (filters: InboxFilters) => ["inbox", "list", filters] as const,
  item: (id: string) => ["inbox", "item", id] as const,
};

export function useInboxItems(filters: InboxFilters) {
  return useQuery({
    queryKey: inboxKeys.all,
    queryFn: () => apiClient.get<InboxItemRead[]>("/inbox/items"),
    select: (items) => {
      const search = filters.search.trim().toLocaleLowerCase();
      return items.filter((item) => {
        const matchesSearch =
          !search ||
          item.actual_filename.toLocaleLowerCase().includes(search) ||
          item.display_name?.toLocaleLowerCase().includes(search);
        const matchesStatus = filters.status === "all" || item.status === filters.status;
        const matchesType = filters.mediaType === "all" || item.media_type === filters.mediaType;
        return Boolean(matchesSearch && matchesStatus && matchesType);
      });
    },
  });
}
