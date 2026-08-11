import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { ApiError, apiClient } from "../shared/api/client";
import type { MediaAssetRead } from "../shared/api/types";
import { MediaPreview } from "../shared/components/MediaPreview";
import { AssetDrawer } from "./AssetDrawer";
import { LibraryFilters } from "./LibraryFilters";
import type { LibraryFilterValues } from "./LibraryFilters";


const initialFilters: LibraryFilterValues = {
  search: "",
  authorId: "",
  seriesId: "",
  tagId: "",
  mediaType: "",
};

function libraryEndpoint(filters: LibraryFilterValues): string {
  const query = new URLSearchParams();
  if (filters.search.trim()) query.set("search", filters.search.trim());
  if (filters.authorId) query.set("author_id", filters.authorId);
  if (filters.seriesId) query.set("series_id", filters.seriesId);
  if (filters.tagId) query.set("tag_id", filters.tagId);
  if (filters.mediaType) query.set("media_type", filters.mediaType);
  const suffix = query.toString();
  return suffix ? `/library/assets?${suffix}` : "/library/assets";
}

export function LibraryPage() {
  const [filters, setFilters] = useState(initialFilters);
  const [activeId, setActiveId] = useState<string | null>(null);
  const assets = useQuery({
    queryKey: ["library", "list", filters],
    queryFn: () => apiClient.get<MediaAssetRead[]>(libraryEndpoint(filters)),
  });
  const items = assets.data ?? [];
  const activeAsset = items.find((asset) => asset.id === activeId) ?? null;

  return (
    <section className={`library-page${activeAsset ? " has-drawer" : ""}`}>
      <header className="page-heading">
        <div>
          <p className="eyebrow">02 / LIBRARY</p>
          <h1>文件库</h1>
          <p>按名称、作者、系列、标签或媒体类型查找已经正式入库的文件。</p>
        </div>
        <span className="library-total">{items.length.toString().padStart(2, "0")} / RESULTS</span>
      </header>
      <LibraryFilters value={filters} onChange={setFilters} />
      {assets.isLoading && <div className="loading-panel">正在读取正式文件库…</div>}
      {assets.error && (
        <div className="inline-error" role="alert">
          {assets.error instanceof ApiError ? assets.error.message : "无法读取正式文件库。"}
        </div>
      )}
      {!assets.isLoading && !assets.error && items.length === 0 && (
        <div className="empty-state">
          <span aria-hidden="true">00</span>
          <strong>没有符合当前条件的正式文件</strong>
          <p>清空筛选条件，或先从待入库页面完成一次入库。</p>
        </div>
      )}
      {items.length > 0 && (
        <div className="media-grid library-grid" aria-label="正式文件">
          {items.map((asset) => (
            <article key={asset.id} className={`media-card library-card${activeId === asset.id ? " is-active" : ""}`}>
              <button
                type="button"
                className="card-open"
                aria-label={`打开 ${asset.actual_filename}`}
                onClick={() => setActiveId(asset.id)}
              >
                <div className="card-preview">
                  <MediaPreview
                    src={`/api/library/assets/${asset.id}/preview`}
                    alt={`${asset.actual_filename} 的预览`}
                    mediaType={asset.media_type}
                  />
                  <span className="asset-type">{asset.media_type === "video" ? "VIDEO" : "IMAGE"}</span>
                </div>
                <div className="card-copy">
                  <strong>{asset.display_name || asset.actual_filename}</strong>
                  {asset.display_name && <span>{asset.actual_filename}</span>}
                  <small>{new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium" }).format(new Date(asset.ingested_at))}</small>
                </div>
              </button>
            </article>
          ))}
        </div>
      )}
      {activeAsset && <AssetDrawer asset={activeAsset} onClose={() => setActiveId(null)} />}
    </section>
  );
}
