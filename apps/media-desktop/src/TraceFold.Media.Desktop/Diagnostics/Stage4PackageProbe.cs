namespace TraceFold.Media.Desktop.Diagnostics;

public sealed record Stage4PackageProbeResult(
    bool Succeeded,
    bool RegisteredReplacePassed,
    bool ContentKeepBothPassed,
    bool UnmanagedReplacePassed,
    bool ReopenPassed,
    bool ProcessedMediaPassed,
    int ImageAssetCount,
    int VideoAssetCount,
    int TotalAssetCount,
    string? ToolPath,
    string? ErrorCode);

public sealed record Stage4CrashProbeResult(
    bool Succeeded,
    bool CrashRecoveryPassed,
    string FaultPoint,
    Guid? AssetId,
    string? TargetSha256,
    string? ErrorCode);

public static class Stage4PackageProbe
{
    public static async Task<int> RunAsync(
        Stage4PackageProbeOptions supplied,
        string? mediaToolRootOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        PackageProbeOptionResult validation = PackageProbeOptions.Parse(supplied.ToArguments());
        if (validation.State != PackageProbeOptionState.Ready || validation.Request is null)
        {
            return 2;
        }

        return await PackageProbeRunner.RunAsync(
            validation.Request,
            mediaToolRootOverride,
            cancellationToken);
    }
}
