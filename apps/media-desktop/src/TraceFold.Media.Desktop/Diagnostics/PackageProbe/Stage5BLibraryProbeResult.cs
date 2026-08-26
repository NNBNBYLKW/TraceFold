namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage5BPackageProbeResult(
    bool Succeeded,
    bool StrictIntersectionPassed,
    bool AtomicBatchPassed,
    bool NamingPreviewMatchedApply,
    bool UndoPassed,
    bool PhysicalFilesUnchanged,
    bool InternalStorageClean,
    string? ErrorCode);
