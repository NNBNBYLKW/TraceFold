import type { InboxItemRead } from "../shared/api/types";
import { MediaCard } from "./MediaCard";


export interface MediaGridProps {
  items: InboxItemRead[];
  selectedIds: Set<string>;
  activeId: string | null;
  onToggle: (item: InboxItemRead) => void;
  onOpen: (item: InboxItemRead) => void;
}

export function MediaGrid({
  items,
  selectedIds,
  activeId,
  onToggle,
  onOpen,
}: MediaGridProps) {
  return (
    <div className="media-grid" aria-label="待入库文件">
      {items.map((item) => (
        <MediaCard
          key={item.id}
          item={item}
          selected={selectedIds.has(item.id)}
          active={activeId === item.id}
          onToggle={onToggle}
          onOpen={onOpen}
        />
      ))}
    </div>
  );
}
