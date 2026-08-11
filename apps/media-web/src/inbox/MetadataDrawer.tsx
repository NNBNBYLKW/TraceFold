import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import type { RefObject } from "react";

import { ApiError, apiClient } from "../shared/api/client";
import type {
  InboxItemRead,
  IngestResultRead,
  MetadataPatch,
  ProcessingJobRead,
} from "../shared/api/types";
import { MediaPreview } from "../shared/components/MediaPreview";
import { TaxonomyPicker } from "../shared/components/TaxonomyPicker";
import { inboxKeys } from "./queries";
import { formatInboxStatus } from "./status";


export interface MetadataDrawerProps {
  item: InboxItemRead;
  onClose: () => void;
  ingestButtonRef?: RefObject<HTMLButtonElement | null>;
  onConflict?: (conflictId: string) => void;
  onCompleted?: (itemId: string) => void;
}

export function MetadataDrawer({
  item,
  onClose,
  ingestButtonRef,
  onConflict,
  onCompleted,
}: MetadataDrawerProps) {
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(item.display_name ?? "");
  const [authorIds, setAuthorIds] = useState(item.author_ids);
  const [seriesIds, setSeriesIds] = useState(item.series_id ? [item.series_id] : []);
  const [tagIds, setTagIds] = useState(item.tag_ids);
  const canEdit = item.allowed_actions.includes("edit_metadata");

  useEffect(() => {
    setDisplayName(item.display_name ?? "");
    setAuthorIds(item.author_ids);
    setSeriesIds(item.series_id ? [item.series_id] : []);
    setTagIds(item.tag_ids);
  }, [item]);

  const save = useMutation({
    mutationFn: (payload: MetadataPatch) =>
      apiClient.patch<InboxItemRead>(`/inbox/items/${item.id}/metadata`, payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: inboxKeys.all });
    },
  });
  const process = useMutation({
    mutationFn: (command: { idempotencyKey: string }) =>
      apiClient.post<ProcessingJobRead>(
        `/inbox/items/${item.id}/process`,
        undefined,
        command,
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: inboxKeys.all });
    },
  });
  const ingest = useMutation({
    mutationFn: (command: { idempotencyKey: string }) =>
      apiClient.post<IngestResultRead>(
        `/inbox/items/${item.id}/ingest`,
        undefined,
        command,
      ),
    onSuccess: async (result) => {
      if (result.kind === "conflict" && result.conflict_id) {
        onConflict?.(result.conflict_id);
        return;
      }
      onCompleted?.(item.id);
      await queryClient.invalidateQueries({ queryKey: inboxKeys.all });
    },
  });
  const commandError = save.error ?? process.error ?? ingest.error;

  return (
    <aside className="metadata-drawer" aria-label="文件资料">
      <header className="drawer-header">
        <div>
          <p className="eyebrow">ITEM / {item.id.slice(0, 8)}</p>
          <h2>{item.display_name || item.actual_filename}</h2>
        </div>
        <button type="button" className="icon-button" onClick={onClose} aria-label="关闭资料面板">×</button>
      </header>
      <div className="drawer-preview">
        <MediaPreview
          src={item.preview_url}
          alt={`${item.actual_filename} 的大图预览`}
          mediaType={item.media_type}
        />
      </div>
      <div className="drawer-state-row">
        <span className={`status-badge status-${item.status}`}>{formatInboxStatus(item)}</span>
        {item.progress_percent !== null && <span>{item.progress_percent}%</span>}
      </div>
      {item.error && (
        <div className="inline-error" role="alert">
          <strong>{item.error.code}</strong>
          <span>{item.error.message}</span>
        </div>
      )}
      <form
        className="metadata-form"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate({
            display_name: displayName.trim() || null,
            author_ids: authorIds,
            series_id: seriesIds[0] ?? null,
            tag_ids: tagIds,
          });
        }}
      >
        <label>
          <span>实际文件名</span>
          <input value={item.actual_filename} readOnly />
        </label>
        <label>
          <span>显示名称</span>
          <input
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
            disabled={!canEdit}
          />
        </label>
        <TaxonomyPicker
          kind="authors"
          label="作者"
          selectedIds={authorIds}
          multiple
          disabled={!canEdit}
          onChange={setAuthorIds}
        />
        <TaxonomyPicker
          kind="series"
          label="系列"
          selectedIds={seriesIds}
          multiple={false}
          disabled={!canEdit}
          onChange={setSeriesIds}
        />
        <TaxonomyPicker
          kind="tags"
          label="内容标签"
          selectedIds={tagIds}
          multiple
          disabled={!canEdit}
          onChange={setTagIds}
        />
        {canEdit && (
          <button type="submit" className="secondary-button" disabled={save.isPending}>
            {save.isPending ? "保存中…" : "保存资料"}
          </button>
        )}
      </form>
      {commandError && (
        <p className="command-error" role="alert">
          {commandError instanceof ApiError ? commandError.message : "操作未能完成。"}
        </p>
      )}
      <footer className="drawer-actions">
        {(item.allowed_actions.includes("process") || item.allowed_actions.includes("retry")) && (
          <button
            type="button"
            className="secondary-button"
            disabled={process.isPending}
            onClick={() => process.mutate({ idempotencyKey: crypto.randomUUID() })}
          >
            {item.allowed_actions.includes("retry") ? "重试处理" : "开始处理"}
          </button>
        )}
        {item.allowed_actions.includes("ingest") && (
          <button
            ref={ingestButtonRef}
            type="button"
            className="primary-button"
            disabled={ingest.isPending}
            onClick={() => ingest.mutate({ idempotencyKey: crypto.randomUUID() })}
          >
            {ingest.isPending ? "正在入库…" : "正式入库"}
          </button>
        )}
      </footer>
    </aside>
  );
}
