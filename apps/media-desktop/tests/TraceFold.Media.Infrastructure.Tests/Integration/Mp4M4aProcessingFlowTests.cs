using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Domain.Inbox;
using TraceFold.Media.Domain.Processing;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Integration;

public sealed class Mp4M4aProcessingFlowTests
{
    [Fact]
    public async Task Real_stream_copy_produces_verified_output_and_preserves_sources()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        string videoSource = Path.Combine(folders.Inbox, "paired.mp4");
        string audioSource = Path.Combine(folders.Inbox, "paired.m4a");
        File.Copy(Fixture("paired.mp4"), videoSource);
        File.Copy(Fixture("paired.m4a"), audioSource);
        var hasher = new Sha256FileHasher();
        var workspace = new ControlledProcessingWorkspace();
        var processor = new Mp4M4aMergeProcessor(
            new FfmpegMediaTool(new BundledMediaToolLocator(
                new MediaToolOptions("C:\\Program Files\\ffmpeg\\bin"))),
            workspace,
            new ProcessingFileVerifier(hasher),
            hasher);
        ProcessingCandidate candidate = await CandidateAsync(
            videoSource,
            audioSource,
            hasher);
        Guid jobId = Guid.NewGuid();
        ProcessingPlanDefinition plan = processor.CreatePlan(jobId, candidate);
        var context = new ProcessingExecutionContext(
            new ProcessingJobRecord(
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
                null),
            folders.Value);

        ProcessedOutputDraft output = await processor.ExecuteAsync(
            context,
            new InlineProgress(),
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(output.FullPath));
        Assert.True(output.HasVideo);
        Assert.True(output.HasAudio);
        Assert.True(File.Exists(videoSource));
        Assert.True(File.Exists(audioSource));
        Assert.StartsWith(
            Path.Combine(folders.Work, ".tracefold", "processing"),
            output.FullPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessingCandidate> CandidateAsync(
        string video,
        string audio,
        Sha256FileHasher hasher) =>
        new(
            Guid.NewGuid(),
            InboxItemState.RequiresProcessing,
            MediaKind.Video,
            "paired.mp4",
            "mp4_m4a_merge",
            [
                await SourceAsync(video, isPrimary: true, hasher),
                await SourceAsync(audio, isPrimary: false, hasher)
            ],
            PreviousAttempts: 0);

    private static async Task<ProcessingCandidateSource> SourceAsync(
        string path,
        bool isPrimary,
        Sha256FileHasher hasher)
    {
        var info = new FileInfo(path);
        return new ProcessingCandidateSource(
            Guid.NewGuid(),
            info.FullName,
            info.Name,
            info.Extension,
            info.Length,
            info.LastWriteTimeUtc,
            await hasher.ComputeSha256Async(
                path,
                TestContext.Current.CancellationToken),
            isPrimary);
    }

    private static string Fixture(string filename)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null
            && !File.Exists(Path.Combine(current.FullName, ".git"))
            && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(
            current.FullName,
            "apps",
            "media-api",
            "tests",
            "fixtures",
            "media",
            filename);
    }

    private sealed class InlineProgress : IProgress<ProcessingProgressUpdate>
    {
        public void Report(ProcessingProgressUpdate value)
        {
        }
    }
}
