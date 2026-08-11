import type { MediaAssetRead } from "../../src/shared/api/types";


export const libraryAsset: MediaAssetRead = {
  id: "asset-1",
  media_type: "video",
  actual_filename: "hangzhou-night.mp4",
  relative_path: "hangzhou-night.mp4",
  display_name: "杭州夜游",
  author_ids: ["author-1"],
  series_id: "series-1",
  tag_ids: ["tag-1"],
  size: 15_728_640,
  ingested_at: "2026-08-10T08:30:00+08:00",
};

export const assetWithoutDisplayName: MediaAssetRead = {
  ...libraryAsset,
  id: "asset-2",
  media_type: "image",
  actual_filename: "west-lake.jpg",
  relative_path: "west-lake.jpg",
  display_name: null,
};
