using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Application.Processing;

public sealed record MediaProcessorDescriptor(
    string Key,
    string Version,
    string Capability,
    int Priority,
    string DisplayName);

public sealed record ProcessingCandidateSource(
    Guid SourceFileId,
    string FullPath,
    string Filename,
    string Extension,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string? Sha256,
    bool IsPrimary);

public sealed record ProcessingCandidate(
    Guid InboxItemId,
    InboxItemState State,
    MediaKind MediaKind,
    string ActualFilename,
    string RequiredCapability,
    IReadOnlyList<ProcessingCandidateSource> Sources,
    int PreviousAttempts);

public sealed record ProcessingPlanInput(
    Guid SourceFileId,
    string Role,
    string FullPath,
    string Filename,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record ProcessingPlanDefinition(
    Guid Id,
    Guid JobId,
    Guid InboxItemId,
    string ProcessorKey,
    string ProcessorVersion,
    string ProcessorDisplayName,
    string Capability,
    string OutputFilename,
    string WorkDirectoryRelativePath,
    IReadOnlyList<string> StepKeys,
    IReadOnlyList<ProcessingPlanInput> Inputs);

public sealed record PlannedProcessing(
    Guid JobId,
    int Attempt,
    ProcessingPlanDefinition Plan);

public sealed record QueueProcessingResult(
    ProcessingJobRecord Job,
    bool Created);

public sealed record ProcessingProgressUpdate(
    string StepKey,
    int Percent);

public sealed record MediaStreamInfo(
    string CodecType,
    TimeSpan? Duration);

public sealed record MediaToolAvailability(
    bool Available,
    string? Version,
    string? ErrorCode);

public sealed record MediaProbeResult(
    TimeSpan Duration,
    IReadOnlyList<MediaStreamInfo> Streams)
{
    public bool HasVideo => Streams.Any(stream => stream.CodecType == "video");

    public bool HasAudio => Streams.Any(stream => stream.CodecType == "audio");
}

public sealed record FfmpegRequest(
    IReadOnlyList<string> Arguments,
    string OutputPath,
    TimeSpan ExpectedDuration);

public sealed record FfmpegResult(
    int ExitCode,
    string DiagnosticTail);

public sealed record ProcessingWorkspacePaths(
    string RootDirectory,
    string PartialOutputPath,
    string FinalOutputPath,
    string FinalOutputRelativePath);

public sealed record ProcessedOutputDraft(
    string FullPath,
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    TimeSpan Duration,
    bool HasVideo,
    bool HasAudio);

public sealed record ProcessedOutputRecord(
    Guid JobId,
    Guid InboxItemId,
    string FullPath,
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    TimeSpan Duration,
    bool HasVideo,
    bool HasAudio,
    bool IsValid);

public sealed record ValidatedProcessingOutput(
    ProcessedOutputRecord Output,
    IReadOnlyList<ProcessingPlanInput> Inputs);

public sealed record ProcessingJobRecord(
    Guid Id,
    Guid InboxItemId,
    int Attempt,
    ProcessingJobStatus Status,
    string? CurrentStepKey,
    int ProgressPercent,
    string? ErrorCode,
    bool IsRetryable,
    string CorrelationId,
    ProcessingPlanDefinition Plan,
    ProcessedOutputRecord? Output);

public class ProcessingException : Exception
{
    public ProcessingException(string errorCode, bool isRetryable)
        : base(errorCode)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }

    public string ErrorCode { get; }

    public bool IsRetryable { get; }
}

public sealed class ProcessingPlanRejectedException : ProcessingException
{
    public ProcessingPlanRejectedException(string errorCode)
        : base(errorCode, false)
    {
    }
}

public sealed class ProcessingCapabilityUnavailableException : ProcessingException
{
    public ProcessingCapabilityUnavailableException()
        : base("processing.capability_unavailable", false)
    {
    }
}

public sealed class MediaToolException : ProcessingException
{
    public MediaToolException(
        string errorCode,
        string diagnosticTail,
        bool isRetryable = true)
        : base(errorCode, isRetryable)
    {
        DiagnosticTail = diagnosticTail;
    }

    public string DiagnosticTail { get; }
}

public sealed record ProcessingExecutionContext(
    ProcessingJobRecord Job,
    LibraryFolders Folders);

public interface IMediaProcessor
{
    MediaProcessorDescriptor Descriptor { get; }

    ProcessingPlanDefinition CreatePlan(
        Guid jobId,
        ProcessingCandidate candidate);

    Task<ProcessedOutputDraft> ExecuteAsync(
        ProcessingExecutionContext context,
        IProgress<ProcessingProgressUpdate> progress,
        CancellationToken cancellationToken);
}

public interface IMediaProcessorRegistry
{
    IMediaProcessor Resolve(string capability);
}

public interface IProcessingPlanner
{
    PlannedProcessing Create(ProcessingCandidate candidate);
}

public interface IProcessingStore
{
    Task<ProcessingCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ProcessingJobRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<QueueProcessingResult> QueueAsync(
        PlannedProcessing planned,
        CancellationToken cancellationToken);

    Task<ProcessingJobRecord?> TryClaimNextAsync(
        CancellationToken cancellationToken);

    Task UpdateProgressAsync(
        Guid jobId,
        ProcessingProgressUpdate update,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid jobId,
        ProcessedOutputDraft output,
        CancellationToken cancellationToken);

    Task<bool> CancelQueuedAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task InterruptAsync(
        Guid jobId,
        string errorCode,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid jobId,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(
        string errorCode,
        CancellationToken cancellationToken);

    Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<Guid?> InvalidateForSourceChangeAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IMediaTool
{
    Task<MediaToolAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken);

    Task<MediaProbeResult> ProbeAsync(
        string trustedPath,
        CancellationToken cancellationToken);

    Task<FfmpegResult> RunFfmpegAsync(
        FfmpegRequest request,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}

public interface IProcessingWorkspace
{
    Task<ProcessingWorkspacePaths> PrepareAsync(
        Guid jobId,
        LibraryFolders folders,
        string outputFilename,
        CancellationToken cancellationToken);

    Task<string> PromoteVerifiedAsync(
        Guid jobId,
        LibraryFolders folders,
        ProcessingWorkspacePaths paths,
        CancellationToken cancellationToken);

    Task CleanupUnverifiedAsync(
        Guid jobId,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}

public interface IProcessingFileVerifier
{
    Task<bool> MatchesAsync(
        LibraryFolders folders,
        ProcessingPlanInput expected,
        CancellationToken cancellationToken);
}

public enum ProcessingCommandOutcome
{
    Queued,
    AlreadyActive,
    Cancelled,
    StopRequested,
    NotEligible,
    SourceMissing,
    SourceChanged,
    ToolUnavailable,
    ShuttingDown,
    Failed
}

public sealed record ProcessingCommandResult(
    ProcessingCommandOutcome Outcome,
    Guid? JobId,
    string Message);

public interface IProcessingCommandService
{
    Task<ProcessingCommandResult> StartAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ProcessingCommandResult> CancelQueuedAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ProcessingCommandResult> StopAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ProcessingCommandResult> RetryAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IProcessingQueueSignal
{
    void Signal();

    Task WaitAsync(CancellationToken cancellationToken);
}

public interface IProcessingJobExecutor
{
    Task<bool> ExecuteNextAsync(CancellationToken cancellationToken);
}

public interface IProcessingRecoveryService
{
    Task ReconcileAsync(CancellationToken cancellationToken);
}

public interface IProcessingExecutionController
{
    IDisposable Register(Guid jobId, CancellationTokenSource source);

    bool Cancel(Guid jobId);
}

public interface IProcessingInvalidationService
{
    Task InvalidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}
