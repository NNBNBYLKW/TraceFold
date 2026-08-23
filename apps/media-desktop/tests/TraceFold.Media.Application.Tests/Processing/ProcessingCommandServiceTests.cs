using System.Security.Cryptography;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class ProcessingCommandServiceTests
{
    [Fact]
    public async Task Start_hashes_sources_queues_once_and_signals_the_worker()
    {
        using var files = new CandidateFiles();
        ProcessingCandidate candidate = files.Candidate();
        var store = new CommandStore(candidate);
        var signal = new CommandSignal();
        var service = new ProcessingCommandService(
            store,
            new CommandPlanner(),
            new AvailableTool(),
            new TestHasher(),
            new MediaOperationCoordinator(),
            signal,
            new CommandController(),
            new CommandActivity());

        ProcessingCommandResult first = await service.StartAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken);
        ProcessingCommandResult duplicate = await service.StartAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingCommandOutcome.Queued, first.Outcome);
        Assert.Equal(ProcessingCommandOutcome.AlreadyActive, duplicate.Outcome);
        Assert.Equal(1, store.QueueCount);
        Assert.Equal(1, signal.SignalCount);
        Assert.All(store.QueuedPlan!.Plan.Inputs,
            input => Assert.Matches("^[0-9A-F]{64}$", input.Sha256));
    }

    [Fact]
    public async Task Stop_running_cancels_only_the_current_job()
    {
        using var files = new CandidateFiles();
        ProcessingCandidate candidate = files.Candidate() with
        {
            State = InboxItemState.Processing
        };
        var store = new CommandStore(candidate);
        ProcessingJobRecord running = CommandPlanner.Job(candidate, ProcessingJobStatus.Running);
        store.Active = running;
        var controller = new CommandController();
        var service = new ProcessingCommandService(
            store,
            new CommandPlanner(),
            new AvailableTool(),
            new TestHasher(),
            new MediaOperationCoordinator(),
            new CommandSignal(),
            controller,
            new CommandActivity());

        ProcessingCommandResult result = await service.StopAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingCommandOutcome.StopRequested, result.Outcome);
        Assert.Equal([running.Id], controller.CancelledJobIds);
    }

    [Fact]
    public async Task Source_change_before_hashing_does_not_queue()
    {
        using var files = new CandidateFiles();
        ProcessingCandidate candidate = files.Candidate();
        await File.AppendAllTextAsync(
            candidate.Sources[0].FullPath,
            "changed",
            TestContext.Current.CancellationToken);
        var store = new CommandStore(candidate);
        var service = new ProcessingCommandService(
            store,
            new CommandPlanner(),
            new AvailableTool(),
            new TestHasher(),
            new MediaOperationCoordinator(),
            new CommandSignal(),
            new CommandController(),
            new CommandActivity());

        ProcessingCommandResult result = await service.StartAsync(
            candidate.InboxItemId,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessingCommandOutcome.SourceChanged, result.Outcome);
        Assert.Equal(0, store.QueueCount);
    }

    private sealed class CandidateFiles : IDisposable
    {
        public CandidateFiles()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"tracefold-command-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Video = Path.Combine(Root, "paired.mp4");
            Audio = Path.Combine(Root, "paired.m4a");
            File.WriteAllBytes(Video, [1, 2, 3]);
            File.WriteAllBytes(Audio, [4, 5]);
        }

        public string Root { get; }

        public string Video { get; }

        public string Audio { get; }

        public ProcessingCandidate Candidate()
        {
            FileInfo video = new(Video);
            FileInfo audio = new(Audio);
            return new ProcessingCandidate(
                Guid.NewGuid(),
                InboxItemState.RequiresProcessing,
                MediaKind.Video,
                "paired.mp4",
                "mp4_m4a_merge",
                [
                    new ProcessingCandidateSource(
                        Guid.NewGuid(), Video, video.Name, video.Extension,
                        video.Length, video.LastWriteTimeUtc, null, true),
                    new ProcessingCandidateSource(
                        Guid.NewGuid(), Audio, audio.Name, audio.Extension,
                        audio.Length, audio.LastWriteTimeUtc, null, false)
                ],
                0);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class CommandStore(ProcessingCandidate candidate)
        : IProcessingStore
    {
        public int QueueCount { get; private set; }

        public PlannedProcessing? QueuedPlan { get; private set; }

        public ProcessingJobRecord? Active { get; set; }

        public Task<ProcessingCandidate?> LoadCandidateAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult<ProcessingCandidate?>(candidate);

        public Task<ProcessingJobRecord?> FindActiveAsync(
            Guid inboxItemId,
            CancellationToken cancellationToken) => Task.FromResult(Active);

        public Task<QueueProcessingResult> QueueAsync(
            PlannedProcessing planned,
            CancellationToken cancellationToken)
        {
            QueueCount++;
            QueuedPlan = planned;
            Active = CommandPlanner.Job(
                candidate,
                ProcessingJobStatus.Queued,
                planned);
            return Task.FromResult(new QueueProcessingResult(Active, true));
        }

        public Task<ProcessingJobRecord?> TryClaimNextAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ProcessingJobRecord?>(null);

        public Task UpdateProgressAsync(Guid jobId, ProcessingProgressUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(Guid jobId, ProcessedOutputDraft output, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> CancelQueuedAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task InterruptAsync(Guid jobId, string errorCode, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailAsync(Guid jobId, string errorCode, bool isRetryable, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(string errorCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessingJobRecord>>([]);

        public Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.FromResult<ValidatedProcessingOutput?>(null);

        public Task<Guid?> InvalidateForSourceChangeAsync(Guid inboxItemId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);
    }

    private sealed class CommandPlanner : IProcessingPlanner
    {
        public PlannedProcessing Create(ProcessingCandidate candidate)
        {
            Guid jobId = Guid.NewGuid();
            var plan = new ProcessingPlanDefinition(
                Guid.NewGuid(), jobId, candidate.InboxItemId,
                "mp4_m4a_merge", "1", "合并视频和音频", "mp4_m4a_merge",
                candidate.ActualFilename,
                Path.Combine(".tracefold", "processing", jobId.ToString("N")),
                ["merge"],
                candidate.Sources.Select((source, index) => new ProcessingPlanInput(
                    source.SourceFileId, index == 0 ? "video" : "audio",
                    source.FullPath, source.Filename, source.Size,
                    source.LastWriteTimeUtc, source.Sha256!)).ToArray());
            return new PlannedProcessing(jobId, candidate.PreviousAttempts + 1, plan);
        }

        public static ProcessingJobRecord Job(
            ProcessingCandidate candidate,
            ProcessingJobStatus status,
            PlannedProcessing? planned = null)
        {
            planned ??= new CommandPlanner().Create(candidate with
            {
                Sources = candidate.Sources.Select(source => source with
                {
                    Sha256 = source.Sha256 ?? new string('A', 64)
                }).ToArray()
            });
            return new ProcessingJobRecord(
                planned.JobId, candidate.InboxItemId, planned.Attempt, status,
                null, 0, null, false, Guid.NewGuid().ToString("N"),
                planned.Plan, null);
        }
    }

    private sealed class AvailableTool : IMediaTool
    {
        public Task<MediaToolAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MediaToolAvailability(true, "fake", null));

        public Task<MediaProbeResult> ProbeAsync(string trustedPath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FfmpegResult> RunFfmpegAsync(FfmpegRequest request, IProgress<int> progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestHasher : IFileHasher
    {
        public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }
    }

    private sealed class CommandSignal : IProcessingQueueSignal
    {
        public int SignalCount { get; private set; }

        public void Signal() => SignalCount++;

        public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CommandController : IProcessingExecutionController
    {
        public List<Guid> CancelledJobIds { get; } = [];

        public IDisposable Register(Guid jobId, CancellationTokenSource source) => throw new NotSupportedException();

        public bool Cancel(Guid jobId)
        {
            CancelledJobIds.Add(jobId);
            return true;
        }
    }

    private sealed class CommandActivity : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public void Publish(InboxActivityKind kind) =>
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
