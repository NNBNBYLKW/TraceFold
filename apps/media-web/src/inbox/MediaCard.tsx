import type { InboxItemRead } from "../shared/api/types";
import { MediaPreview } from "../shared/components/MediaPreview";
import { formatInboxStatus } from "./status";

export interface MediaCardProps {
  item: InboxItemRead;
  selected: boolean;
  active: boolean;
  onToggle: (item: InboxItemRead) => void;
  onOpen: (item: InboxItemRead) => void;
}

export function MediaCard({ item, selected, active, onToggle, onOpen }: MediaCardProps) {
  const status = formatInboxStatus(item);
  return (
    <article className={`media-card status-${item.status}${active ? " is-active" : ""}`}>
      <label className="card-selector">
        <input
          type="checkbox"
          checked={selected}
          onChange={() => onToggle(item)}
          aria-label={`选择 ${item.actual_filename}`}
        />
        <span aria-hidden="true" />
      </label>
      <button
        type="button"
        className="card-open"
        onClick={() => onOpen(item)}
        aria-label={`打开 ${item.actual_filename}`}
      >
        <div className="card-preview">
          <MediaPreview
            src={item.preview_url}
            alt={`${item.actual_filename} 的预览`}
            mediaType={item.media_type}
          />
          <span className={`status-badge status-${item.status}`}>{status}</span>
        </div>
        <div className="card-copy">
          <strong>{item.display_name || item.actual_filename}</strong>
          {item.display_name && <span>{item.actual_filename}</span>}
          {item.error && <small className="card-error">{item.error.message}</small>}
        </div>
      </button>
    </article>
  );
}
