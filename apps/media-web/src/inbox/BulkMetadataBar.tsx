import { useMutation } from "@tanstack/react-query";
import { useState } from "react";

import { apiClient } from "../shared/api/client";
import type { BulkMetadataPatch, InboxItemRead } from "../shared/api/types";
import { TaxonomyPicker } from "../shared/components/TaxonomyPicker";


export interface BulkMetadataBarProps {
  items: InboxItemRead[];
  onApplied: () => void;
}

export function BulkMetadataBar({ items, onApplied }: BulkMetadataBarProps) {
  const [authorIds, setAuthorIds] = useState<string[]>([]);
  const [tagIds, setTagIds] = useState<string[]>([]);
  const [seriesIds, setSeriesIds] = useState<string[]>([]);
  const [replaceSeries, setReplaceSeries] = useState(false);
  const editable = items.every((item) => item.allowed_actions.includes("edit_metadata"));
  const mutation = useMutation({
    mutationFn: (payload: BulkMetadataPatch) =>
      apiClient.patch<{ updated_count: number }>("/inbox/items/bulk-metadata", payload),
    onSuccess: onApplied,
  });
  return (
    <aside className="bulk-bar" aria-label="批量标注">
      <div className="bulk-heading">
        <span>{String(items.length).padStart(2, "0")}</span>
        <div>
          <strong>批量标注</strong>
          <small>作者和标签只追加，不覆盖已有内容</small>
        </div>
      </div>
      <TaxonomyPicker
        kind="authors"
        label="批量作者"
        selectedIds={authorIds}
        multiple
        onChange={setAuthorIds}
      />
      <TaxonomyPicker
        kind="tags"
        label="批量标签"
        selectedIds={tagIds}
        multiple
        onChange={setTagIds}
      />
      <TaxonomyPicker
        kind="series"
        label="批量系列"
        selectedIds={seriesIds}
        multiple={false}
        onChange={(ids) => {
          setSeriesIds(ids);
          setReplaceSeries(true);
        }}
      />
      {replaceSeries && <p className="bulk-warning">系列将替换所选文件的现有系列</p>}
      {!editable && <p className="bulk-warning">所选文件中包含当前不可编辑的项目</p>}
      <button
        type="button"
        className="primary-button"
        disabled={!editable || mutation.isPending}
        onClick={() => mutation.mutate({
          item_ids: items.map((item) => item.id),
          append_author_ids: authorIds,
          append_tag_ids: tagIds,
          replace_series: replaceSeries,
          replace_series_id: seriesIds[0] ?? null,
        })}
      >
        {mutation.isPending ? "正在应用…" : `应用到 ${items.length} 项`}
      </button>
    </aside>
  );
}
