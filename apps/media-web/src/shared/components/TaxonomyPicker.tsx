import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useId, useMemo, useState } from "react";

import { apiClient } from "../api/client";
import type { TaxonomyRead } from "../api/types";


type TaxonomyKind = "authors" | "series" | "tags";

export interface TaxonomyPickerProps {
  kind: TaxonomyKind;
  label: string;
  selectedIds: string[];
  multiple: boolean;
  disabled?: boolean;
  onChange: (ids: string[]) => void;
}

export function TaxonomyPicker({
  kind,
  label,
  selectedIds,
  multiple,
  disabled = false,
  onChange,
}: TaxonomyPickerProps) {
  const inputId = useId();
  const [search, setSearch] = useState("");
  const queryClient = useQueryClient();
  const taxonomy = useQuery({
    queryKey: ["taxonomy", kind],
    queryFn: () => apiClient.get<TaxonomyRead[]>(`/${kind}`),
  });
  const create = useMutation({
    mutationFn: (name: string) =>
      apiClient.post<TaxonomyRead>(`/${kind}`, { name }),
    onSuccess: async (entry) => {
      await queryClient.invalidateQueries({ queryKey: ["taxonomy", kind] });
      onChange(multiple ? [...new Set([...selectedIds, entry.id])] : [entry.id]);
      setSearch("");
    },
  });
  const entries = taxonomy.data ?? [];
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const visible = useMemo(
    () => entries.filter((entry) =>
      !normalizedSearch || entry.display_name.toLocaleLowerCase().includes(normalizedSearch),
    ),
    [entries, normalizedSearch],
  );
  const exactMatch = entries.some(
    (entry) => entry.display_name.toLocaleLowerCase() === normalizedSearch,
  );

  if (!multiple) {
    return (
      <div className="taxonomy-field">
        <label htmlFor={inputId}>{label}</label>
        <select
          id={inputId}
          disabled={disabled}
          value={selectedIds[0] ?? ""}
          onChange={(event) => onChange(event.target.value ? [event.target.value] : [])}
        >
          <option value="">不设置系列</option>
          {entries
            .filter((entry) => selectedIds.includes(entry.id) || visible.includes(entry))
            .map((entry) => (
            <option key={entry.id} value={entry.id}>{entry.display_name}</option>
          ))}
        </select>
        <input
          aria-label={`搜索或创建${label}`}
          value={search}
          disabled={disabled}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={`搜索或创建${label}`}
        />
        {search.trim() && !exactMatch && (
          <button
            type="button"
            className="taxonomy-create"
            disabled={disabled || create.isPending}
            onClick={() => create.mutate(search.trim())}
          >
            创建“{search.trim()}”
          </button>
        )}
      </div>
    );
  }

  return (
    <fieldset className="taxonomy-picker" disabled={disabled}>
      <legend>{label}</legend>
      <label className="taxonomy-search" htmlFor={inputId}>
        <span className="sr-only">搜索或创建{label}</span>
        <input
          id={inputId}
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={`搜索或创建${label}`}
        />
      </label>
      <div className="taxonomy-options">
        {visible.map((entry) => (
          <label key={entry.id} className="taxonomy-option">
            <input
              type="checkbox"
              checked={selectedIds.includes(entry.id)}
              onChange={(event) => {
                onChange(
                  event.target.checked
                    ? [...selectedIds, entry.id]
                    : selectedIds.filter((id) => id !== entry.id),
                );
              }}
            />
            <span>{entry.display_name}</span>
          </label>
        ))}
      </div>
      {search.trim() && !exactMatch && (
        <button
          type="button"
          className="taxonomy-create"
          disabled={create.isPending}
          onClick={() => create.mutate(search.trim())}
        >
          创建“{search.trim()}”
        </button>
      )}
    </fieldset>
  );
}
