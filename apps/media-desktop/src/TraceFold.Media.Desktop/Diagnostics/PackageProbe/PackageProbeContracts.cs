using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public enum PackageProbeOptionState { NotRequested, Ready, Invalid }

public enum PackageProbeScenarioKind
{
    Stage4Conflict,
    Stage4CrashSeed,
    Stage4CrashResume,
    Stage5ABatch,
    Stage5BLibrary,
    Stage5CTaxonomy,
    Stage5CBDiagnostics
}

public sealed record PackageProbeRequest(
    string Root,
    string OwnerToken,
    PackageProbeScenarioKind Scenario,
    IngestFaultPoint? FaultPoint,
    Stage4CrashProbeManifest? CrashManifest);

public sealed record PackageProbeOptionResult(
    PackageProbeOptionState State,
    PackageProbeRequest? Request,
    string? ErrorCode);

public sealed record Stage5ABatchProbeResult(
    bool Succeeded,
    bool AtomicMetadata,
    bool GenericProcessingQueued,
    bool ProcessingCompleted,
    int CompletedIngestCount,
    int ConflictCount,
    bool ConflictSourceRetained,
    bool InternalStorageClean,
    string? ErrorCode);

public interface IPackageProbeScenario
{
    PackageProbeScenarioKind Kind { get; }

    Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken);
}
