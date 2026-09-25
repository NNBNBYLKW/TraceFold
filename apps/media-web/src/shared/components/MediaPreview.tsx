import { useState } from "react";

import { apiClient } from "../api/client";


export interface MediaPreviewProps {
  src: string;
  alt: string;
  mediaType: "image" | "video";
}

export function MediaPreview({ src, alt, mediaType }: MediaPreviewProps) {
  const [failed, setFailed] = useState(false);
  if (failed) {
    return (
      <div className="media-placeholder" role="img" aria-label={`${alt}，暂无预览`}>
        <span aria-hidden="true">{mediaType === "video" ? "▶" : "▧"}</span>
        <small>PREVIEW UNAVAILABLE</small>
      </div>
    );
  }
  return (
    <img
      className="media-preview"
      src={apiClient.url(src)}
      alt={alt}
      crossOrigin="anonymous"
      loading="lazy"
      onError={() => setFailed(true)}
    />
  );
}
