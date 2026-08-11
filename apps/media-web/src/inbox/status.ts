import type { InboxItemRead } from "../shared/api/types";


const labels: Record<InboxItemRead["status"], string> = {
  discovered: "已发现",
  analyzing: "分析中",
  processing: "合并中",
  ready_to_tag: "待标注",
  conflict: "等待冲突处理",
  ingesting: "入库中",
  completed: "已入库",
  failed: "处理失败",
};

export function formatInboxStatus(item: InboxItemRead): string {
  if (item.status === "processing" && item.progress_percent !== null) {
    return `${labels.processing} ${item.progress_percent}%`;
  }
  return labels[item.status];
}
