import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { ApiError, apiClient } from "../shared/api/client";
import type { MediaAssetRead, MetadataPatch } from "../shared/api/types";
import { MediaPreview } from "../shared/components/MediaPreview";
import { TaxonomyPicker } from "../shared/components/TaxonomyPicker";


interface AssetDrawerProps {
  asset: MediaAssetRead;
  onClose: () => void;
}

export function AssetDrawer({ asset, onClose }: AssetDrawerProps) {
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(asset.display_name ?? "");
  const [authorIds, setAuthorIds] = useState(asset.author_ids);
  const [seriesIds, setSeriesIds] = useState(asset.series_id ? [asset.series_id] : []);
  const [tagIds, setTagIds] = useState(asset.tag_ids);

  useEffect(() => {
    setDisplayName(asset.display_name ?? "");
    setAuthorIds(asset.author_ids);
    setSeriesIds(asset.series_id ? [asset.series_id] : []);
    setTagIds(asset.tag_ids);
  }, [asset]);

  const save = useMutation({
    mutationFn: (payload: MetadataPatch) =>
      apiClient.patch<MediaAssetRead>(`/library/assets/${asset.id}/metadata`, payload),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["library"] }),
        queryClient.invalidateQueries({ queryKey: ["library", "item", asset.id] }),
      ]);
    },
  });
  const reveal = useMutation({
    mutationFn: () =>
      apiClient.post<{ asset_id: string; revealed: boolean }>(
        `/library/assets/${asset.id}/reveal`,
      ),
  });
  const error = save.error ?? reveal.error;

  return (
    <aside className="metadata-drawer asset-drawer" aria-label="正式文件资料">
      <header className="drawer-header">
        <div>
          <p className="eyebrow">ASSET / {asset.id.slice(0, 8)}</p>
          <h2>{asset.display_name || asset.actual_filename}</h2>
        </div>
        <button type="button" className="icon-button" onClick={onClose} aria-label="关闭资料面板">×</button>
      </header>
      <div className="drawer-preview">
        <MediaPreview
          src={`/api/library/assets/${asset.id}/preview`}
          alt={`${asset.actual_filename} 的正式库预览`}
          mediaType={asset.media_type}
        />
      </div>
      <div className="asset-summary">
        <span>{asset.media_type === "video" ? "视频" : "图片"}</span>
        <span>{new Intl.NumberFormat("zh-CN").format(asset.size)} B</span>
      </div>
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
          <input value={asset.actual_filename} readOnly />
        </label>
        <label>
          <span>库内相对位置</span>
          <input value={asset.relative_path} readOnly />
        </label>
        <label>
          <span>显示名称</span>
          <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        </label>
        <TaxonomyPicker kind="authors" label="作者" selectedIds={authorIds} multiple onChange={setAuthorIds} />
        <TaxonomyPicker kind="series" label="系列" selectedIds={seriesIds} multiple={false} onChange={setSeriesIds} />
        <TaxonomyPicker kind="tags" label="内容标签" selectedIds={tagIds} multiple onChange={setTagIds} />
        <button type="submit" className="secondary-button" disabled={save.isPending}>
          {save.isPending ? "保存中…" : "保存资料"}
        </button>
      </form>
      {error && (
        <p className="command-error" role="alert">
          {error instanceof ApiError ? error.message : "操作未能完成。"}
        </p>
      )}
      <footer className="drawer-actions">
        <button
          type="button"
          className="primary-button"
          disabled={reveal.isPending}
          onClick={() => reveal.mutate()}
        >
          {reveal.isPending ? "正在打开…" : "打开所在目录"}
        </button>
      </footer>
    </aside>
  );
}
