namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage5CBDiagnosticsProbeResult(
    bool Succeeded,
    bool AllCategoriesObserved,
    bool PackagedEnvironmentHealthy,
    bool FailureIsolationPassed,
    bool RedactionPassed,
    bool BusinessDataUnchanged,
    bool SettingsUnchanged,
    bool BackgroundStateUnchanged,
    bool MediaFilesUnchanged,
    bool TemporaryArtifactsClean,
    bool ToolProcessesClean,
    string? ErrorCode);
