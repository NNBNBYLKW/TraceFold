using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Application.Settings;
using TraceFold.Media.Application.Storage;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using Xunit;

namespace TraceFold.Media.Application.Tests.Processing;

public sealed class Mp4M4aMergeProcessorTests
{
    [Fact]
    public void CreatePlan_maps_one_primary_mp4_and_one_m4a()
    {
        ProcessingCandidate candidate = PairedCandidate();
        using var workspace = new FakeWorkspace();
        var processor = new Mp4M4aMergeProcessor(
            new FakeMediaTool(),
            workspace,
            new FakeVerifier(),
            new FakeHasher());

        ProcessingPlanDefinition plan = processor.CreatePlan(
            Guid.NewGuid(),
            candidate);

        Assert.Equal(["video", "audio"],
            plan.Inputs.Select(input => input.Role));
        Assert.Equal(candidate.ActualFilename, plan.OutputFilename);
        Assert.Equal(
            ["check_sources", "merge", "verify_output"],
            plan.StepKeys);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void CreatePlan_rejects_ambiguous_or_incomplete_pairs(
        int mp4Count,
        int m4aCount)
    {
        using var workspace = new FakeWorkspace();
        Mp4M4aMergeProcessor processor = CreateProcessor(
            workspace: workspace);

        Assert.Throws<ProcessingPlanRejectedException>(() =>
            processor.CreatePlan(
                Guid.NewGuid(),
                PairCounts(mp4Count, m4aCount)));
    }

    [Fact]
    public async Task Execute_reports_fixed_step_ranges_and_returns_verified_output()
    {
        using var workspace = new FakeWorkspace();
        var updates = new InlineProgress<ProcessingProgressUpdate>();
        var tools = new FakeMediaTool();
        Mp4M4aMergeProcessor processor = CreateProcessor(tools, workspace);

        ProcessedOutputDraft output = await processor.ExecuteAsync(
            Context(workspace),
            updates,
            TestContext.Current.CancellationToken);

        Assert.Contains(updates.Values,
            update => update.StepKey == "check_sources" && update.Percent == 5);
        Assert.Contains(updates.Values,
            update => update.StepKey == "merge"
                && update.Percent is >= 5 and <= 95);
        Assert.Contains(updates.Values,
            update => update.StepKey == "verify_output" && update.Percent == 99);
        Assert.True(output.HasVideo);
        Assert.True(output.HasAudio);
        Assert.True(File.Exists(output.FullPath));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Execute_rejects_output_missing_either_stream(
        bool video,
        bool audio)
    {
        using var workspace = new FakeWorkspace();
        Mp4M4aMergeProcessor processor = CreateProcessor(
            new FakeMediaTool(video, audio),
            workspace);

        ProcessingException error = await Assert.ThrowsAsync<ProcessingException>(() =>
            processor.ExecuteAsync(
                Context(workspace),
                new InlineProgress<ProcessingProgressUpdate>(),
                TestContext.Current.CancellationToken));

        Assert.Equal("processing.output_streams_invalid", error.ErrorCode);
        Assert.True(workspace.CleanupCalled);
    }

    [Fact]
    public async Task Execute_uses_the_fixed_safe_ffmpeg_argument_order()
    {
        using var workspace = new FakeWorkspace();
        var tools = new FakeMediaTool();
        Mp4M4aMergeProcessor processor = CreateProcessor(tools, workspace);

        await processor.ExecuteAsync(
            Context(workspace),
            new InlineProgress<ProcessingProgressUpdate>(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "-nostdin", "-n",
                "-i", "C:\\inbox\\paired.mp4",
                "-i", "C:\\inbox\\paired.m4a",
                "-map", "0:v:0",
                "-map", "1:a:0",
                "-c", "copy",
                "-movflags", "+faststart"
            ],
            tools.LastRequest!.Arguments);
        Assert.DoesNotContain(tools.LastRequest.OutputPath, tools.LastRequest.Arguments);
    }

    [Fact]
    public async Task Changed_source_cleans_workspace_without_running_ffmpeg()
    {
        using var workspace = new FakeWorkspace();
        var tools = new FakeMediaTool();
        Mp4M4aMergeProcessor processor = CreateProcessor(
            tools,
            workspace,
            new FakeVerifier(matches: false));

        ProcessingException error = await Assert.ThrowsAsync<ProcessingException>(() =>
            processor.ExecuteAsync(
                Context(workspace),
                new InlineProgress<ProcessingProgressUpdate>(),
                TestContext.Current.CancellationToken));

        Assert.Equal("processing.source_changed", error.ErrorCode);
        Assert.Equal(0, tools.RunCount);
        Assert.True(workspace.CleanupCalled);
    }

    private static Mp4M4aMergeProcessor CreateProcessor(
        FakeMediaTool? tools = null,
        FakeWorkspace? workspace = null,
        FakeVerifier? verifier = null) =>
        new(
            tools ?? new FakeMediaTool(),
            workspace ?? new FakeWorkspace(),
            verifier ?? new FakeVerifier(),
            new FakeHasher());

    private static ProcessingCandidate PairedCandidate() =>
        PairCounts(1, 1);

    private static ProcessingCandidate PairCounts(int mp4Count, int m4aCount)
    {
        var sources = new List<ProcessingCandidateSource>();
        for (int index = 0; index < mp4Count; index++)
        {
            sources.Add(Source(
                $"C:\\inbox\\paired{index}.mp4",
                ".mp4",
                isPrimary: index == 0));
        }

        for (int index = 0; index < m4aCount; index++)
        {
            sources.Add(Source(
                $"C:\\inbox\\paired{index}.m4a",
                ".m4a",
                isPrimary: false));
        }

        if (mp4Count == 1)
        {
            sources[0] = sources[0] with
            {
                FullPath = "C:\\inbox\\paired.mp4",
                Filename = "paired.mp4"
            };
        }

        if (m4aCount == 1)
        {
            int audioIndex = sources.Count - 1;
            sources[audioIndex] = sources[audioIndex] with
            {
                FullPath = "C:\\inbox\\paired.m4a",
                Filename = "paired.m4a"
            };
        }

        return new ProcessingCandidate(
            Guid.NewGuid(),
            InboxItemState.RequiresProcessing,
            MediaKind.Video,
            "paired.mp4",
            "mp4_m4a_merge",
            sources,
            PreviousAttempts: 0);
    }

    private static ProcessingCandidateSource Source(
        string path,
        string extension,
        bool isPrimary) =>
        new(
            Guid.NewGuid(),
            path,
            Path.GetFileName(path),
            extension,
            10,
            DateTimeOffset.Parse("2026-08-23T10:00:00Z"),
            new string(isPrimary ? 'A' : 'B', 64),
            isPrimary);

    private static ProcessingExecutionContext Context(FakeWorkspace workspace)
    {
        Guid jobId = Guid.NewGuid();
        ProcessingCandidate candidate = PairedCandidate();
        ProcessingPlanDefinition plan = new Mp4M4aMergeProcessor(
            new FakeMediaTool(),
            workspace,
            new FakeVerifier(),
            new FakeHasher()).CreatePlan(jobId, candidate);
        var job = new ProcessingJobRecord(
            jobId,
            candidate.InboxItemId,
            1,
            ProcessingJobStatus.Running,
            null,
            0,
            null,
            false,
            Guid.NewGuid().ToString("N"),
            plan,
            null);
        return new ProcessingExecutionContext(
            job,
            new LibraryFolders(
                "C:\\inbox",
                "C:\\library",
                workspace.Root));
    }

    private sealed class FakeMediaTool(
        bool outputHasVideo = true,
        bool outputHasAudio = true) : IMediaTool
    {
        public FfmpegRequest? LastRequest { get; private set; }

        public int RunCount { get; private set; }

        public Task<MediaToolAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaToolAvailability(true, "fake", null));

        public Task<MediaProbeResult> ProbeAsync(
            string trustedPath,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MediaStreamInfo> streams = trustedPath.EndsWith(
                ".m4a",
                StringComparison.OrdinalIgnoreCase)
                ? [new MediaStreamInfo("audio", TimeSpan.FromSeconds(2))]
                : trustedPath.EndsWith(
                    "merged.partial.mp4",
                    StringComparison.OrdinalIgnoreCase)
                    ? OutputStreams()
                    : [new MediaStreamInfo("video", TimeSpan.FromSeconds(2))];
            return Task.FromResult(new MediaProbeResult(
                TimeSpan.FromSeconds(2),
                streams));
        }

        public async Task<FfmpegResult> RunFfmpegAsync(
            FfmpegRequest request,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            RunCount++;
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            await File.WriteAllBytesAsync(
                request.OutputPath,
                [1, 2, 3],
                cancellationToken);
            progress.Report(50);
            progress.Report(100);
            return new FfmpegResult(0, string.Empty);
        }

        private IReadOnlyList<MediaStreamInfo> OutputStreams()
        {
            var streams = new List<MediaStreamInfo>();
            if (outputHasVideo)
            {
                streams.Add(new MediaStreamInfo("video", TimeSpan.FromSeconds(2)));
            }

            if (outputHasAudio)
            {
                streams.Add(new MediaStreamInfo("audio", TimeSpan.FromSeconds(2)));
            }

            return streams;
        }
    }

    private sealed class FakeWorkspace : IProcessingWorkspace, IDisposable
    {
        public FakeWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"tracefold-processor-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public bool CleanupCalled { get; private set; }

        public Task<ProcessingWorkspacePaths> PrepareAsync(
            Guid jobId,
            LibraryFolders folders,
            string outputFilename,
            CancellationToken cancellationToken)
        {
            string jobRoot = Directory.CreateDirectory(Path.Combine(
                Root,
                jobId.ToString("N"))).FullName;
            string partial = Path.Combine(jobRoot, "merged.partial.mp4");
            string final = Path.Combine(jobRoot, outputFilename);
            return Task.FromResult(new ProcessingWorkspacePaths(
                jobRoot,
                partial,
                final,
                Path.GetRelativePath(Root, final)));
        }

        public Task<string> PromoteVerifiedAsync(
            Guid jobId,
            LibraryFolders folders,
            ProcessingWorkspacePaths paths,
            CancellationToken cancellationToken)
        {
            File.Move(paths.PartialOutputPath, paths.FinalOutputPath, overwrite: false);
            return Task.FromResult(paths.FinalOutputPath);
        }

        public Task CleanupUnverifiedAsync(
            Guid jobId,
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            CleanupCalled = true;
            string path = Path.Combine(Root, jobId.ToString("N"));
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FakeVerifier(bool matches = true) : IProcessingFileVerifier
    {
        public Task<bool> MatchesAsync(
            LibraryFolders folders,
            ProcessingPlanInput expected,
            CancellationToken cancellationToken) => Task.FromResult(matches);
    }

    private sealed class FakeHasher : IFileHasher
    {
        public Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult(new string('C', 64));
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
