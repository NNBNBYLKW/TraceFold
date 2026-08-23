using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;

namespace TraceFold.Media.Application.Processing;

public sealed class ProcessingCommandService(
    IProcessingStore store,
    IProcessingPlanner planner,
    IMediaTool mediaTool,
    IFileHasher fileHasher,
    IMediaOperationCoordinator coordinator,
    IProcessingQueueSignal signal,
    IProcessingExecutionController controller,
    IInboxActivityNotifier activity) : IProcessingCommandService
{
    public Task<ProcessingCommandResult> StartAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        StartCoreAsync(inboxItemId, isRetry: false, cancellationToken);

    public Task<ProcessingCommandResult> RetryAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        StartCoreAsync(inboxItemId, isRetry: true, cancellationToken);

    public async Task<ProcessingCommandResult> CancelQueuedAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using IMediaOperationLease lease = coordinator.Begin(cancellationToken);
            ProcessingJobRecord? active = await store.FindActiveAsync(
                inboxItemId,
                lease.CancellationToken);
            if (active?.Status != ProcessingJobStatus.Queued)
            {
                return Result(ProcessingCommandOutcome.NotEligible, active?.Id,
                    "当前项目不在排队中。");
            }

            bool cancelled = await store.CancelQueuedAsync(
                active.Id,
                lease.CancellationToken);
            if (!cancelled)
            {
                return Result(ProcessingCommandOutcome.NotEligible, active.Id,
                    "排队任务状态已变化。");
            }

            activity.Publish(InboxActivityKind.ItemChanged);
            return Result(ProcessingCommandOutcome.Cancelled, active.Id, "已取消排队。");
        }
        catch (MediaOperationRejectedException)
        {
            return Result(ProcessingCommandOutcome.ShuttingDown, null, "软件正在关闭。");
        }
    }

    public async Task<ProcessingCommandResult> StopAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using IMediaOperationLease lease = coordinator.Begin(cancellationToken);
            ProcessingJobRecord? active = await store.FindActiveAsync(
                inboxItemId,
                lease.CancellationToken);
            if (active?.Status != ProcessingJobStatus.Running)
            {
                return Result(ProcessingCommandOutcome.NotEligible, active?.Id,
                    "当前项目未在处理中。");
            }

            bool requested = controller.Cancel(active.Id);
            return requested
                ? Result(ProcessingCommandOutcome.StopRequested, active.Id, "正在停止处理。")
                : Result(ProcessingCommandOutcome.NotEligible, active.Id, "处理任务已经结束。");
        }
        catch (MediaOperationRejectedException)
        {
            return Result(ProcessingCommandOutcome.ShuttingDown, null, "软件正在关闭。");
        }
    }

    private async Task<ProcessingCommandResult> StartCoreAsync(
        Guid inboxItemId,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            await using IMediaOperationLease lease = coordinator.Begin(cancellationToken);
            ProcessingJobRecord? active = await store.FindActiveAsync(
                inboxItemId,
                lease.CancellationToken);
            if (active is not null)
            {
                return Result(
                    ProcessingCommandOutcome.AlreadyActive,
                    active.Id,
                    "该项目已有处理任务。");
            }

            MediaToolAvailability availability = await mediaTool.CheckAvailabilityAsync(
                lease.CancellationToken);
            if (!availability.Available)
            {
                return Result(
                    ProcessingCommandOutcome.ToolUnavailable,
                    null,
                    "前置处理工具不可用。");
            }

            ProcessingCandidate? candidate = await store.LoadCandidateAsync(
                inboxItemId,
                lease.CancellationToken);
            if (candidate is null || !Eligible(candidate, isRetry))
            {
                return Result(
                    ProcessingCommandOutcome.NotEligible,
                    null,
                    "当前项目不能开始前置处理。");
            }

            var hashedSources = new List<ProcessingCandidateSource>(
                candidate.Sources.Count);
            foreach (ProcessingCandidateSource source in candidate.Sources)
            {
                FileInfo before = new(source.FullPath);
                if (!before.Exists)
                {
                    return Result(
                        ProcessingCommandOutcome.SourceMissing,
                        null,
                        "源文件已不存在。");
                }

                if (!Matches(before, source))
                {
                    return Result(
                        ProcessingCommandOutcome.SourceChanged,
                        null,
                        "源文件已经变化，请先刷新。");
                }

                string sha256 = await fileHasher.ComputeSha256Async(
                    source.FullPath,
                    lease.CancellationToken);
                before.Refresh();
                if (!Matches(before, source))
                {
                    return Result(
                        ProcessingCommandOutcome.SourceChanged,
                        null,
                        "源文件已经变化，请先刷新。");
                }

                hashedSources.Add(source with { Sha256 = sha256.ToUpperInvariant() });
            }

            ProcessingCandidate hashed = candidate with { Sources = hashedSources };
            PlannedProcessing planned = planner.Create(hashed);
            QueueProcessingResult queued = await store.QueueAsync(
                planned,
                lease.CancellationToken);
            if (!queued.Created)
            {
                return Result(
                    ProcessingCommandOutcome.AlreadyActive,
                    queued.Job.Id,
                    "该项目已有处理任务。");
            }

            activity.Publish(InboxActivityKind.ItemChanged);
            signal.Signal();
            return Result(
                ProcessingCommandOutcome.Queued,
                queued.Job.Id,
                "已加入处理队列。");
        }
        catch (MediaOperationRejectedException)
        {
            return Result(ProcessingCommandOutcome.ShuttingDown, null, "软件正在关闭。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result(ProcessingCommandOutcome.ShuttingDown, null, "软件正在关闭。");
        }
        catch (ProcessingException error)
        {
            return Result(ProcessingCommandOutcome.Failed, null, error.ErrorCode);
        }
        catch (IOException)
        {
            return Result(ProcessingCommandOutcome.SourceChanged, null, "源文件无法读取。");
        }
        catch (UnauthorizedAccessException)
        {
            return Result(ProcessingCommandOutcome.SourceChanged, null, "源文件无法读取。");
        }
    }

    private static bool Eligible(ProcessingCandidate candidate, bool isRetry) =>
        !string.IsNullOrWhiteSpace(candidate.RequiredCapability)
        && (isRetry
            ? candidate.State == InboxItemState.ProcessingInterrupted
                || (candidate.State == InboxItemState.Failed)
            : candidate.State == InboxItemState.RequiresProcessing);

    private static bool Matches(
        FileInfo file,
        ProcessingCandidateSource source) =>
        file.Exists
        && file.Length == source.Size
        && new DateTimeOffset(file.LastWriteTimeUtc) == source.LastWriteTimeUtc;

    private static ProcessingCommandResult Result(
        ProcessingCommandOutcome outcome,
        Guid? jobId,
        string message) => new(outcome, jobId, message);
}
