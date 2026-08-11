import type { InboxItemRead, TaxonomyRead } from "../../src/shared/api/types";


const baseItem: InboxItemRead = {
  id: "item-base",
  media_type: "video",
  status: "ready_to_tag",
  allowed_actions: ["edit_metadata", "ingest"],
  actual_filename: "base.mp4",
  display_name: null,
  author_ids: [],
  series_id: null,
  tag_ids: [],
  sources: [],
  preview_url: "/api/inbox/items/item-base/preview",
  progress_percent: null,
  error: null,
};

export const readyItem: InboxItemRead = {
  ...baseItem,
  id: "item-1",
  actual_filename: "旅行记录_杭州_01.mp4",
  display_name: "杭州旅行",
  author_ids: ["author-1"],
  tag_ids: ["tag-1"],
  preview_url: "/api/inbox/items/item-1/preview",
};

export const processingItem: InboxItemRead = {
  ...baseItem,
  id: "item-2",
  status: "processing",
  allowed_actions: [],
  actual_filename: "城市漫游.mp4",
  preview_url: "/api/inbox/items/item-2/preview",
  progress_percent: 68,
};

export const failedItem: InboxItemRead = {
  ...baseItem,
  id: "item-3",
  status: "failed",
  allowed_actions: ["retry"],
  actual_filename: "损坏片段.mp4",
  preview_url: "/api/inbox/items/item-3/preview",
  error: {
    code: "PROCESSOR_INPUT_INVALID",
    message: "文件无法完成前置处理。",
  },
};

export const secondReadyItem: InboxItemRead = {
  ...baseItem,
  id: "item-4",
  media_type: "image",
  actual_filename: "杭州夜景.jpg",
  preview_url: "/api/inbox/items/item-4/preview",
};

export const authors: TaxonomyRead[] = [
  { id: "author-1", display_name: "林同学" },
  { id: "author-2", display_name: "陈老师" },
];

export const series: TaxonomyRead[] = [
  { id: "series-1", display_name: "城市漫游" },
];

export const tags: TaxonomyRead[] = [
  { id: "tag-1", display_name: "夜景" },
  { id: "tag-2", display_name: "旅行" },
];
