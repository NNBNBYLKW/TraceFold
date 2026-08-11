export type MediaType = "image" | "video";

export type InboxStatus =
  | "discovered"
  | "analyzing"
  | "processing"
  | "ready_to_tag"
  | "conflict"
  | "ingesting"
  | "completed"
  | "failed";

export type InboxAction =
  | "process"
  | "retry"
  | "edit_metadata"
  | "ingest"
  | "resolve_conflict";

export interface SourceFileSummary {
  id: string;
  filename: string;
  extension: string;
  size: number;
  is_primary: boolean;
}

export interface InboxItemRead {
  id: string;
  media_type: MediaType;
  status: InboxStatus;
  allowed_actions: InboxAction[];
  actual_filename: string;
  display_name: string | null;
  author_ids: string[];
  series_id: string | null;
  tag_ids: string[];
  sources: SourceFileSummary[];
  preview_url: string;
  progress_percent: number | null;
  error: { code: string; message: string } | null;
}

export interface TaxonomyRead {
  id: string;
  display_name: string;
}

export interface MetadataPatch {
  display_name: string | null;
  author_ids: string[];
  series_id: string | null;
  tag_ids: string[];
}

export interface BulkMetadataPatch {
  item_ids: string[];
  append_author_ids: string[];
  append_tag_ids: string[];
  replace_series: boolean;
  replace_series_id: string | null;
}

export interface IngestResultRead {
  kind: "completed" | "conflict";
  operation_id: string;
  asset_id: string | null;
  conflict_id: string | null;
  cleanup_warning: string | null;
}

export interface ProcessingJobRead {
  id: string;
  inbox_item_id: string;
  status: "queued" | "running" | "completed" | "failed";
  attempt_count: number;
  current_step: number;
  error_code: string | null;
  user_message: string | null;
}

export interface ApiErrorEnvelope {
  error: {
    code: string;
    message: string;
    details: Record<string, unknown>;
  };
}
