using System.Diagnostics;
using TraceFold.Media.Application.Processing;
using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.MediaTools;

public sealed class FfmpegMediaToolIntegrationTests
{
    private const string InstalledRoot = "C:\\Program Files\\ffmpeg\\bin";

    [Fact]
    public async Task Availability_reports_the_explicit_installed_version()
    {
        FfmpegMediaTool tool = CreateInstalled();

        MediaToolAvailability availability = await tool.CheckAvailabilityAsync(
            TestContext.Current.CancellationToken);

        Assert.True(availability.Available);
        Assert.StartsWith("ffmpeg version", availability.Version);
        Assert.Null(availability.ErrorCode);
    }

    [Fact]
    public async Task Probe_reads_the_fixed_video_and_audio_fixtures()
    {
        FfmpegMediaTool tool = CreateInstalled();

        MediaProbeResult video = await tool.ProbeAsync(
            Fixture("paired.mp4"),
            TestContext.Current.CancellationToken);
        MediaProbeResult audio = await tool.ProbeAsync(
            Fixture("paired.m4a"),
            TestContext.Current.CancellationToken);

        Assert.True(video.HasVideo);
        Assert.True(audio.HasAudio);
        Assert.True(video.Duration > TimeSpan.Zero);
        Assert.True(audio.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Run_uses_argument_list_and_reports_completion()
    {
        using TestDirectory outputRoot = TestDirectory.Create();
        string output = Path.Combine(outputRoot.Path, "copied.mp4");
        FfmpegMediaTool tool = CreateInstalled();
        var progress = new InlineProgress();
        var request = new FfmpegRequest(
            [
                "-nostdin", "-n",
                "-i", Fixture("paired.mp4"),
                "-map", "0:v:0",
                "-c", "copy"
            ],
            output,
            TimeSpan.FromSeconds(1));

        FfmpegResult result = await tool.RunFfmpegAsync(
            request,
            progress,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output));
        Assert.Contains(100, progress.Values);
    }

    [Fact]
    public async Task Cancellation_waits_for_the_owned_process_to_exit()
    {
        using TestDirectory outputRoot = TestDirectory.Create();
        string output = Path.Combine(outputRoot.Path, "long-running.mp4");
        FfmpegMediaTool tool = CreateInstalled();
        using var cancellation = new CancellationTokenSource();
        var request = new FfmpegRequest(
            [
                "-nostdin", "-n", "-re",
                "-f", "lavfi",
                "-i", "testsrc=size=16x16:rate=1",
                "-t", "120",
                "-c:v", "mpeg4"
            ],
            output,
            TimeSpan.FromSeconds(120));
        Task<FfmpegResult> run = tool.RunFfmpegAsync(
            request,
            new InlineProgress(),
            cancellation.Token);
        await WaitForFileAsync(output, TimeSpan.FromSeconds(10));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        if (File.Exists(output))
        {
            using FileStream exclusive = new(
                output,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.True(exclusive.CanWrite);
        }
    }

    [Fact]
    public async Task Missing_package_tools_return_a_stable_unavailable_result()
    {
        using TestDirectory root = TestDirectory.Create();
        var tool = new FfmpegMediaTool(
            new BundledMediaToolLocator(new MediaToolOptions(root.Path)));

        MediaToolAvailability availability = await tool.CheckAvailabilityAsync(
            TestContext.Current.CancellationToken);

        Assert.False(availability.Available);
        Assert.Equal("processing.tool_missing", availability.ErrorCode);
    }

    private static FfmpegMediaTool CreateInstalled() =>
        new(new BundledMediaToolLocator(new MediaToolOptions(InstalledRoot)));

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
        string path = Path.Combine(
            current.FullName,
            "apps",
            "media-api",
            "tests",
            "fixtures",
            "media",
            filename);
        Assert.True(File.Exists(path), $"Fixture is missing: {path}");
        return path;
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException("FFmpeg did not create its owned output in time.");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private sealed class InlineProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }
}
