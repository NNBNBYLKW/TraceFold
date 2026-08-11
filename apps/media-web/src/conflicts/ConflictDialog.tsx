import { useMutation, useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent, RefObject } from "react";

import { ApiError, apiClient } from "../shared/api/client";
import type {
  ConflictRead,
  ConflictResolution,
  ConflictResolutionRead,
  MediaType,
  TaxonomyRead,
} from "../shared/api/types";
import { MediaPreview } from "../shared/components/MediaPreview";


const conflictKindLabels: Record<string, string> = {
  name: "文件名相同",
  content: "文件内容重复",
  hash: "文件内容重复",
};

interface ConflictDialogProps {
  conflictId: string;
  returnFocusRef: RefObject<HTMLElement | null>;
  onClose: () => void;
  onExpired: () => void;
  onResolved: (
    resolution: ConflictResolution,
    result: ConflictResolutionRead,
  ) => void;
}

interface ComparisonFile {
  side: "existing" | "incoming";
  previewUrl: string | null;
  actualFilename: string;
  displayName: string | null;
  mediaType: MediaType;
  size: number;
  timeLabel: string;
  timeValue: string;
  sourceSummary: string;
  authorIds: string[];
  seriesId: string | null;
  tagIds: string[];
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let amount = value / 1024;
  let unit = units[0];
  for (let index = 1; amount >= 1024 && index < units.length; index += 1) {
    amount /= 1024;
    unit = units[index];
  }
  return `${amount.toFixed(amount >= 10 ? 1 : 2)} ${unit}`;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) return value;
  return new Intl.DateTimeFormat("zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(date);
}

function namesFor(ids: string[], entries: TaxonomyRead[]): string {
  if (ids.length === 0) return "—";
  const names = new Map(entries.map((entry) => [entry.id, entry.display_name]));
  return ids.map((id) => names.get(id) ?? id).join("、");
}

function comparisonFiles(conflict: ConflictRead): [ComparisonFile | null, ComparisonFile] {
  const existing = conflict.existing_asset;
  const incomingSize = conflict.incoming_item.sources.reduce(
    (total, source) => total + source.size,
    0,
  );
  return [
    existing ? {
      side: "existing",
      previewUrl: conflict.existing_preview_url,
      actualFilename: existing.actual_filename,
      displayName: existing.display_name,
      mediaType: existing.media_type,
      size: existing.size,
      timeLabel: "入库时间",
      timeValue: formatTimestamp(existing.ingested_at),
      sourceSummary: "正式文件库",
      authorIds: existing.author_ids,
      seriesId: existing.series_id,
      tagIds: existing.tag_ids,
    } : null,
    {
      side: "incoming",
      previewUrl: conflict.incoming_preview_url,
      actualFilename: conflict.incoming_item.actual_filename,
      displayName: conflict.incoming_item.display_name,
      mediaType: conflict.incoming_item.media_type,
      size: incomingSize,
      timeLabel: "修改时间",
      timeValue: "服务端未提供",
      sourceSummary: `${conflict.incoming_item.sources.length} 个源文件`,
      authorIds: conflict.incoming_item.author_ids,
      seriesId: conflict.incoming_item.series_id,
      tagIds: conflict.incoming_item.tag_ids,
    },
  ];
}

function ComparisonPanel({
  file,
  title,
  previewAlt,
  authors,
  series,
  tags,
}: {
  file: ComparisonFile | null;
  title: string;
  previewAlt: string;
  authors: TaxonomyRead[];
  series: TaxonomyRead[];
  tags: TaxonomyRead[];
}) {
  if (!file) {
    return (
      <section className="conflict-panel is-missing">
        <h3>{title}</h3>
        <div className="media-placeholder" role="img" aria-label={previewAlt}>
          <span aria-hidden="true">—</span>
          <small>NO REGISTERED FILE</small>
        </div>
        <p>没有可供替换的正式文件。</p>
      </section>
    );
  }
  const seriesName = file.seriesId
    ? namesFor([file.seriesId], series)
    : "—";
  return (
    <section className={`conflict-panel is-${file.side}`}>
      <header>
        <span aria-hidden="true">{file.side === "existing" ? "A" : "B"}</span>
        <h3>{title}</h3>
      </header>
      <div className="conflict-preview">
        {file.previewUrl ? (
          <MediaPreview
            src={file.previewUrl}
            alt={previewAlt}
            mediaType={file.mediaType}
          />
        ) : (
          <div className="media-placeholder" role="img" aria-label={previewAlt}>
            <span aria-hidden="true">▧</span>
            <small>PREVIEW UNAVAILABLE</small>
          </div>
        )}
      </div>
      <strong className="conflict-filename">{file.actualFilename}</strong>
      <dl className="conflict-facts">
        <div><dt>文件大小</dt><dd>{formatBytes(file.size)}</dd></div>
        <div><dt>{file.timeLabel}</dt><dd>{file.timeValue}</dd></div>
        <div><dt>媒体信息</dt><dd>{file.mediaType === "video" ? "视频" : "图片"} · {file.sourceSummary}</dd></div>
        <div><dt>显示名称</dt><dd>{file.displayName || "—"}</dd></div>
        <div><dt>作者</dt><dd>{namesFor(file.authorIds, authors)}</dd></div>
        <div><dt>系列</dt><dd>{seriesName}</dd></div>
        <div><dt>标签</dt><dd>{namesFor(file.tagIds, tags)}</dd></div>
      </dl>
    </section>
  );
}

export function ConflictDialog({
  conflictId,
  returnFocusRef,
  onClose,
  onExpired,
  onResolved,
}: ConflictDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const [confirmReplace, setConfirmReplace] = useState(false);
  const conflict = useQuery({
    queryKey: ["conflicts", conflictId],
    queryFn: () => apiClient.get<ConflictRead>(`/conflicts/${conflictId}`),
  });
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
  const resolve = useMutation({
    mutationFn: (command: { resolution: ConflictResolution; idempotencyKey: string }) =>
      apiClient.post<ConflictResolutionRead>(
        `/conflicts/${conflictId}/resolve`,
        { resolution: command.resolution },
        { idempotencyKey: command.idempotencyKey },
      ),
    onSuccess: (result, command) => onResolved(command.resolution, result),
    onError: (error) => {
      if (error instanceof ApiError && error.code === "CONFLICT_CHOICE_EXPIRED") {
        onExpired();
      }
    },
  });

  useEffect(() => {
    const returnTarget = returnFocusRef.current;
    closeRef.current?.focus();
    return () => returnTarget?.focus();
  }, [returnFocusRef]);

  function submit(resolution: ConflictResolution) {
    resolve.mutate({ resolution, idempotencyKey: crypto.randomUUID() });
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      if (!resolve.isPending) onClose();
      return;
    }
    if (event.key !== "Tab") return;
    const focusable = Array.from(
      dialogRef.current?.querySelectorAll<HTMLElement>(
        "button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])",
      ) ?? [],
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable.at(-1);
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  const files = conflict.data ? comparisonFiles(conflict.data) : null;
  const readError = conflict.error instanceof ApiError
    ? conflict.error.message
    : "无法读取冲突详情。";
  const resolutionError = resolve.error instanceof ApiError
    ? resolve.error.message
    : "选择未能提交，请重试。";

  return (
    <div className="dialog-backdrop">
      <div
        ref={dialogRef}
        className="conflict-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="conflict-title"
        onKeyDown={handleKeyDown}
      >
        <header className="conflict-header">
          <div>
            <p className="eyebrow">FILE CONFLICT / {conflictId.slice(0, 8)}</p>
            <h2 id="conflict-title">目标位置已有同名或相同内容的文件</h2>
            <p>比较两个文件，然后选择本次入库应如何处理。</p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="icon-button"
            aria-label="关闭冲突对话框"
            disabled={resolve.isPending}
            onClick={onClose}
          >×</button>
        </header>

        {conflict.isLoading && <div className="conflict-loading">正在准备文件对比…</div>}
        {conflict.error && <div className="inline-error" role="alert">{readError}</div>}
        {conflict.data && files && (
          <>
            <div className="conflict-kinds" aria-label="冲突原因">
              {conflict.data.conflict_kinds.map((kind) => (
                <span key={kind}>{conflictKindLabels[kind] ?? kind}</span>
              ))}
            </div>
            <div className="conflict-comparison">
              <ComparisonPanel
                file={files[0]}
                title="文件库中的文件"
                previewAlt="文件库中的文件"
                authors={authors.data ?? []}
                series={series.data ?? []}
                tags={tags.data ?? []}
              />
              <ComparisonPanel
                file={files[1]}
                title="本次待入库文件"
                previewAlt="本次待入库文件"
                authors={authors.data ?? []}
                series={series.data ?? []}
                tags={tags.data ?? []}
              />
            </div>
          </>
        )}

        {resolve.error && !(resolve.error instanceof ApiError && resolve.error.code === "CONFLICT_CHOICE_EXPIRED") && (
          <p className="command-error" role="alert">{resolutionError}</p>
        )}

        <footer className="conflict-actions">
          {confirmReplace ? (
            <div className="replace-confirmation" role="alert">
              <p>现有文件及其资料将被本次文件替换。此操作无法撤销。</p>
              <button
                type="button"
                className="secondary-button"
                disabled={resolve.isPending}
                onClick={() => setConfirmReplace(false)}
              >返回比较</button>
              <button
                type="button"
                className="danger-button"
                autoFocus
                disabled={resolve.isPending}
                onClick={() => submit("replace")}
              >{resolve.isPending ? "正在替换…" : "确认替换"}</button>
            </div>
          ) : (
            <>
              <button
                type="button"
                className="secondary-button"
                disabled={!conflict.data || resolve.isPending}
                onClick={() => submit("skip")}
              >跳过</button>
              <button
                type="button"
                className="primary-button"
                disabled={!conflict.data || resolve.isPending}
                onClick={() => submit("keep_both")}
              >{resolve.isPending ? "正在处理…" : "两者都保留"}</button>
              <button
                type="button"
                className="danger-button"
                disabled={!conflict.data || !conflict.data.existing_asset || resolve.isPending}
                onClick={() => setConfirmReplace(true)}
              >替换原文件</button>
            </>
          )}
        </footer>
      </div>
    </div>
  );
}
