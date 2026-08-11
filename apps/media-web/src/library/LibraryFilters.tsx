import { useQuery } from "@tanstack/react-query";

import { apiClient } from "../shared/api/client";
import type { MediaType, TaxonomyRead } from "../shared/api/types";


export interface LibraryFilterValues {
  search: string;
  authorId: string;
  seriesId: string;
  tagId: string;
  mediaType: MediaType | "";
}

interface LibraryFiltersProps {
  value: LibraryFilterValues;
  onChange: (value: LibraryFilterValues) => void;
}

export function LibraryFilters({ value, onChange }: LibraryFiltersProps) {
  const authors = useQuery({
    queryKey: ["taxonomy", "authors"],
    queryFn: () => apiClient.get<TaxonomyRead[]>("/authors"),
  });
  const series = useQuery({
    queryKey: ["taxonomy", "series"],
    queryFn: () => apiClient.get<TaxonomyRead[]>("/series"),
  });
  const tags = useQuery({
    queryKey: ["taxonomy", "tags"],
    queryFn: () => apiClient.get<TaxonomyRead[]>("/tags"),
  });

  function update<Key extends keyof LibraryFilterValues>(
    key: Key,
    next: LibraryFilterValues[Key],
  ) {
    onChange({ ...value, [key]: next });
  }

  return (
    <div className="filter-strip library-filters">
      <label className="search-field">
        <span className="sr-only">搜索正式文件库</span>
        <input
          type="search"
          value={value.search}
          placeholder="搜索实际名称或显示名称"
          onChange={(event) => update("search", event.target.value)}
        />
      </label>
      <label>
        <span>作者</span>
        <select value={value.authorId} onChange={(event) => update("authorId", event.target.value)}>
          <option value="">全部作者</option>
          {(authors.data ?? []).map((entry) => (
            <option key={entry.id} value={entry.id}>{entry.display_name}</option>
          ))}
        </select>
      </label>
      <label>
        <span>系列</span>
        <select value={value.seriesId} onChange={(event) => update("seriesId", event.target.value)}>
          <option value="">全部系列</option>
          {(series.data ?? []).map((entry) => (
            <option key={entry.id} value={entry.id}>{entry.display_name}</option>
          ))}
        </select>
      </label>
      <label>
        <span>标签</span>
        <select value={value.tagId} onChange={(event) => update("tagId", event.target.value)}>
          <option value="">全部标签</option>
          {(tags.data ?? []).map((entry) => (
            <option key={entry.id} value={entry.id}>{entry.display_name}</option>
          ))}
        </select>
      </label>
      <label>
        <span>类型</span>
        <select
          value={value.mediaType}
          onChange={(event) => update("mediaType", event.target.value as MediaType | "")}
        >
          <option value="">全部类型</option>
          <option value="video">视频</option>
          <option value="image">图片</option>
        </select>
      </label>
    </div>
  );
}
