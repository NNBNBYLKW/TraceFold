import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useRef, useState } from "react";

import { ConflictDialog } from "../conflicts/ConflictDialog";
import { ApiError, apiClient } from "../shared/api/client";
import type {
  ConflictResolution,
  InboxItemRead,
  InboxStatus,
  MediaType,
} from "../shared/api/types";
import { BulkMetadataBar } from "./BulkMetadataBar";
import { MediaGrid } from "./MediaGrid";
import { MetadataDrawer } from "./MetadataDrawer";
import { inboxKeys, useInboxItems } from "./queries";


export function InboxPage() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<InboxStatus | "all">("all");
  const [mediaType, setMediaType] = useState<MediaType | "all">("all");
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [activeId, setActiveId] = useState<string | null>(null);
  const [conflictId, setConflictId] = useState<string | null>(null);
  const [conflictOpen, setConflictOpen] = useState(false);
  const [conflictNotice, setConflictNotice] = useState<string | null>(null);
  const ingestButtonRef = useRef<HTMLButtonElement>(null);
  const filters = { search, status, mediaType };
  const inbox = useInboxItems(filters);
  const items = inbox.data ?? [];
  const allItems = queryClient.getQueriesData<InboxItemRead[]>({ queryKey: inboxKeys.all })
    .flatMap(([, value]) => value ?? []);
  const knownItems = allItems.length ? allItems : items;
  const selectedItems = useMemo(
    () => knownItems.filter((item) => selectedIds.has(item.id)),
    [knownItems, selectedIds],
  );
  const activeItem = knownItems.find((item) => item.id === activeId) ?? null;
  const refresh = useMutation({
    mutationFn: () => apiClient.post("/inbox/scan"),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: inboxKeys.all });
    },
  });

  function toggle(item: InboxItemRead) {
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(item.id)) next.delete(item.id);
      else next.add(item.id);
      return next;
    });
  }

  async function handleConflictResolved(resolution: ConflictResolution) {
    const completedItemId = activeId;
    setConflictOpen(false);
    setConflictId(null);
    await queryClient.invalidateQueries({ queryKey: inboxKeys.all });
    if (resolution === "skip") return;
    setActiveId(null);
    if (completedItemId) {
      setSelectedIds((current) => {
        const next = new Set(current);
        next.delete(completedItemId);
        return next;
      });
    }
    await queryClient.invalidateQueries({ queryKey: ["library"] });
  }

  return (
    <section className={`inbox-page${activeItem ? " has-drawer" : ""}`}>
      <header className="page-heading">
        <div>
          <p className="eyebrow">01 / INBOX</p>
          <h1>待入库</h1>
          <p>扫描完成后，选择文件补充作者、系列与内容标签。</p>
        </div>
        <button
          type="button"
          className="primary-button"
          disabled={refresh.isPending}
          onClick={() => refresh.mutate()}
        >
          {refresh.isPending ? "扫描中…" : "手动刷新"}
        </button>
      </header>
      <div className="filter-strip">
        <label className="search-field">
          <span className="sr-only">搜索待入库文件</span>
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="搜索实际名称或显示名称"
          />
        </label>
        <label>
          <span>状态</span>
          <select value={status} onChange={(event) => setStatus(event.target.value as InboxStatus | "all")}>
            <option value="all">全部状态</option>
            <option value="ready_to_tag">待标注</option>
            <option value="processing">处理中</option>
            <option value="conflict">冲突</option>
            <option value="failed">失败</option>
          </select>
        </label>
        <label>
          <span>类型</span>
          <select value={mediaType} onChange={(event) => setMediaType(event.target.value as MediaType | "all")}>
            <option value="all">全部类型</option>
            <option value="video">视频</option>
            <option value="image">图片</option>
          </select>
        </label>
        <span className="result-count">{items.length} 项</span>
      </div>
      {conflictNotice && <p className="context-notice" role="status">{conflictNotice}</p>}
      {conflictId && !conflictOpen && (
        <div className="context-notice" role="status">
          <span>冲突选择已暂时关闭，文件尚未入库。</span>
          <button type="button" className="secondary-button" onClick={() => setConflictOpen(true)}>
            查看冲突
          </button>
        </div>
      )}
      {inbox.isLoading && <div className="loading-panel">正在读取待入库目录…</div>}
      {inbox.error && (
        <div className="inline-error" role="alert">
          {inbox.error instanceof ApiError ? inbox.error.message : "无法读取待入库文件。"}
        </div>
      )}
      {!inbox.isLoading && !inbox.error && items.length === 0 && (
        <div className="empty-state">
          <span aria-hidden="true">00</span>
          <strong>待入库目录中还没有可处理文件</strong>
          <p>将图片或视频放入待入库目录，然后手动刷新。</p>
        </div>
      )}
      {items.length > 0 && (
        <MediaGrid
          items={items}
          selectedIds={selectedIds}
          activeId={activeId}
          onToggle={toggle}
          onOpen={(item) => setActiveId(item.id)}
        />
      )}
      {selectedItems.length >= 2 && (
        <BulkMetadataBar
          items={selectedItems}
          onApplied={() => {
            void queryClient.invalidateQueries({ queryKey: inboxKeys.all });
          }}
        />
      )}
      {activeItem && (
        <MetadataDrawer
          item={activeItem}
          onClose={() => setActiveId(null)}
          ingestButtonRef={ingestButtonRef}
          onConflict={(id) => {
            setConflictNotice(null);
            setConflictId(id);
            setConflictOpen(true);
          }}
          onCompleted={(itemId) => {
            setActiveId(null);
            setSelectedIds((current) => {
              const next = new Set(current);
              next.delete(itemId);
              return next;
            });
            void queryClient.invalidateQueries({ queryKey: ["library"] });
          }}
        />
      )}
      {conflictId && conflictOpen && (
        <ConflictDialog
          conflictId={conflictId}
          returnFocusRef={ingestButtonRef}
          onClose={() => setConflictOpen(false)}
          onExpired={() => {
            setConflictOpen(false);
            setConflictId(null);
            setConflictNotice("源文件已变化，请重新选择。");
            void queryClient.invalidateQueries({ queryKey: inboxKeys.all });
          }}
          onResolved={(resolution) => {
            void handleConflictResolved(resolution);
          }}
        />
      )}
    </section>
  );
}
