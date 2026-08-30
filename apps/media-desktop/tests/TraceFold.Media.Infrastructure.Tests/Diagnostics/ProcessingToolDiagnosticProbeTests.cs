using System.Diagnostics;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class ProcessingToolDiagnosticProbeTests
{
    [Fact]
    public async Task Check_requires_both_tools_and_preserves_both_versions()
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
        var reader = new FakeExecutableVersionReader(new Dictionary<string, ExecutableVersionRead>
        {
            [root.FfmpegPath] = new(0, "ffmpeg version 7.1", false),
            [root.FfprobePath] = new(0, "ffprobe version 7.1", false)
        });

        SystemDiagnosticItem item = Assert.Single(await Probe(root, reader)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
        Assert.Contains(item.Facts, fact => fact.Key == "tools.ffmpeg.version"
            && fact.Value == "ffmpeg version 7.1"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.Contains(item.Facts, fact => fact.Key == "tools.ffprobe.version"
            && fact.Value == "ffprobe version 7.1"
            && fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable);
        Assert.All(reader.Arguments,
            args => Assert.Equal(new[] { "-version" }, args));
        Assert.Equal(2, reader.Arguments.Count);
    }

    [Theory]
    [InlineData(true, false, "diagnostics.tools.ffmpeg_missing")]
    [InlineData(false, true, "diagnostics.tools.ffprobe_missing")]
    public async Task Check_distinguishes_missing_tools(
        bool removeFfmpeg,
        bool removeFfprobe,
        string errorCode)
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
        if (removeFfmpeg)
        {
            File.Delete(root.FfmpegPath);
        }

        if (removeFfprobe)
        {
            File.Delete(root.FfprobePath);
        }

        var reader = new FakeExecutableVersionReader(HealthyReads(root));

        SystemDiagnosticItem item = Assert.Single(await Probe(root, reader)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal(errorCode, item.ErrorCode);
        AssertOrdinaryFilesRemainManageable(item);
    }

    [Fact]
    public async Task Check_reports_an_unsafe_locator_without_exposing_a_path()
    {
        var locator = new BundledMediaToolLocator(
            new MediaToolOptions("relative-tools"));
        var reader = new FakeExecutableVersionReader(
            new Dictionary<string, ExecutableVersionRead>());

        SystemDiagnosticItem item = Assert.Single(await
            new ProcessingToolDiagnosticProbe(locator, reader)
                .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal("diagnostics.tools.ffmpeg_missing", item.ErrorCode);
        Assert.DoesNotContain(item.Facts, fact =>
            fact.Key.EndsWith(".path", StringComparison.Ordinal));
        Assert.Empty(reader.Arguments);
    }

    [Theory]
    [InlineData("ffmpeg", 1, "ffmpeg version 7.1", false,
        "diagnostics.tools.launch_failed")]
    [InlineData("ffprobe", 1, "ffprobe version 7.1", false,
        "diagnostics.tools.launch_failed")]
    [InlineData("ffmpeg", 0, "", false,
        "diagnostics.tools.version_unknown")]
    [InlineData("ffprobe", 0, "not a version", false,
        "diagnostics.tools.version_unknown")]
    [InlineData("ffmpeg", 0, "ffmpeg version 7.1", true,
        "diagnostics.tools.timeout")]
    [InlineData("ffprobe", 0, "ffprobe version 7.1", true,
        "diagnostics.tools.timeout")]
    public async Task Check_maps_independent_tool_failures(
        string failedTool,
        int exitCode,
        string firstLine,
        bool timedOut,
        string errorCode)
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
        var reads = HealthyReads(root);
        reads[failedTool == "ffmpeg" ? root.FfmpegPath : root.FfprobePath] =
            new ExecutableVersionRead(exitCode, firstLine, timedOut);
        var reader = new FakeExecutableVersionReader(reads);

        SystemDiagnosticItem item = Assert.Single(await Probe(root, reader)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
        Assert.Equal(errorCode, item.ErrorCode);
        Assert.Equal(2, reader.Arguments.Count);
        AssertOrdinaryFilesRemainManageable(item);
    }

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("ffprobe")]
    public async Task Check_maps_launch_exceptions_without_disclosing_messages(
        string failedTool)
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
        var reader = new FakeExecutableVersionReader(
            HealthyReads(root),
            failedTool == "ffmpeg" ? root.FfmpegPath : root.FfprobePath,
            new InvalidOperationException("C:\\private\\secret-video.mp4"));

        SystemDiagnosticItem item = Assert.Single(await Probe(root, reader)
            .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal("diagnostics.tools.launch_failed", item.ErrorCode);
        Assert.DoesNotContain("secret-video", item.Conclusion,
            StringComparison.Ordinal);
        Assert.DoesNotContain(item.Facts, fact =>
            fact.Value.Contains("secret-video", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Check_marks_tool_paths_local_only()
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();

        SystemDiagnosticItem item = Assert.Single(await Probe(
            root,
            new FakeExecutableVersionReader(HealthyReads(root)))
            .CheckAsync(TestContext.Current.CancellationToken));

        SystemDiagnosticFact[] pathFacts = [.. item.Facts.Where(fact =>
            fact.Key.EndsWith(".path", StringComparison.Ordinal))];
        Assert.Equal(2, pathFacts.Length);
        Assert.All(pathFacts, fact => Assert.Equal(
            SystemDiagnosticFactSensitivity.LocalOnly,
            fact.Sensitivity));
    }

    [Fact]
    public async Task Check_propagates_caller_cancellation()
    {
        using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new FakeExecutableVersionReader(
            HealthyReads(root),
            root.FfmpegPath,
            new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Probe(root, reader).CheckAsync(cancellation.Token));
    }

    [Fact]
    public async Task Controlled_runner_kills_the_exact_process_tree_on_cancellation()
    {
        int? processId = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ControlledProcessRunner(pid =>
        {
            processId = pid;
            started.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource();
        string command = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Task<ControlledProcessResult> run = runner.RunAsync(
            command,
            ["/d", "/c", "ping -t 127.0.0.1"],
            TimeSpan.FromMinutes(1),
            cancellation.Token);
        await started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.NotNull(processId);
        await AssertProcessExitedAsync(processId.Value, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Controlled_runner_caps_stdout_and_stderr_at_exactly_eight_kibibytes()
    {
        var runner = new ControlledProcessRunner();
        string command = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        ControlledProcessResult result = await runner.RunAsync(
            command,
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "$value='x'*9000; [Console]::Out.Write($value); [Console]::Error.Write($value)"
            ],
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(8 * 1024, result.StandardOutput.Length);
        Assert.Equal(8 * 1024, result.StandardError.Length);
    }

    private static ProcessingToolDiagnosticProbe Probe(
        ToolRoot root,
        IExecutableVersionReader reader) =>
        new(
            new BundledMediaToolLocator(new MediaToolOptions(root.Path)),
            reader);

    private static Dictionary<string, ExecutableVersionRead> HealthyReads(
        ToolRoot root) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [root.FfmpegPath] = new(0, "ffmpeg version 7.1", false),
            [root.FfprobePath] = new(0, "ffprobe version 7.1", false)
        };

    private static void AssertOrdinaryFilesRemainManageable(
        SystemDiagnosticItem item)
    {
        Assert.Contains("普通图片", item.Impact, StringComparison.Ordinal);
        Assert.Contains("无需前置处理", item.Impact, StringComparison.Ordinal);
    }

    private static async Task AssertProcessExitedAsync(int processId, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Process {processId} was still running after cancellation.");
    }

    private sealed class FakeExecutableVersionReader(
        IReadOnlyDictionary<string, ExecutableVersionRead> reads,
        string? failurePath = null,
        Exception? failure = null) : IExecutableVersionReader
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public Task<ExecutableVersionRead> ReadAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Arguments.Add([.. arguments]);
            if (string.Equals(executable, failurePath,
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException<ExecutableVersionRead>(failure!);
            }

            return Task.FromResult(reads[executable]);
        }
    }

    private sealed class ToolRoot : IDisposable
    {
        private readonly TestDirectory _root;

        private ToolRoot(TestDirectory root)
        {
            _root = root;
        }

        public string Path => _root.Path;

        public string FfmpegPath => System.IO.Path.Combine(Path, "ffmpeg.exe");

        public string FfprobePath => System.IO.Path.Combine(Path, "ffprobe.exe");

        public static ToolRoot WithPlaceholderExecutables()
        {
            var root = new ToolRoot(TestDirectory.Create());
            File.WriteAllBytes(root.FfmpegPath, [1]);
            File.WriteAllBytes(root.FfprobePath, [2]);
            return root;
        }

        public void Dispose() => _root.Dispose();
    }
}
