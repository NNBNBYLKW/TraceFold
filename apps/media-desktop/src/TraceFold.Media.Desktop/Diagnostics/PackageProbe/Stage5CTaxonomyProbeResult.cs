namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage5CTaxonomyProbeResult(
    bool Succeeded,
    bool QueryPassed,
    bool MergePassed,
    bool MixedDeleteBlocked,
    bool DeleteUndoPassed,
    bool MergeUndoPassed,
    bool VersioningPassed,
    bool PhysicalFilesUnchanged,
    bool InternalStorageClean,
    string? ErrorCode);
