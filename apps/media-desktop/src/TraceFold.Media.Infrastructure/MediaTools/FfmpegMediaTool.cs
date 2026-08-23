using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TraceFold.Media.Application.Processing;

namespace TraceFold.Media.Infrastructure.MediaTools;

public sealed class FfmpegMediaTool(BundledMediaToolLocator locator) : IMediaTool
{
    private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    public async Task<MediaToolAvailability> CheckAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        MediaToolLocation? location = locator.TryResolve();
        if (location is null)
        {
            return new MediaToolAvailability(
                Available: false,
                Version: null,
                ErrorCode: "processing.tool_missing");
        }

        try
        {
            CapturedProcess result = await RunCapturedAsync(
                location.FfmpegPath,
                ["-version"],
                AvailabilityTimeout,
                cancellationToken);
            string? version = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(version)
                ? new MediaToolAvailability(true, version, null)
                : new MediaToolAvailability(
                    false,
                    null,
                    "processing.tool_unavailable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is TimeoutException
            or Win32Exception
            or InvalidOperationException
            or IOException)
        {
            return new MediaToolAvailability(
                false,
                null,
                "processing.tool_unavailable");
        }
    }

    public async Task<MediaProbeResult> ProbeAsync(
        string trustedPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trustedPath)
            || !Path.IsPathFullyQualified(trustedPath))
        {
            throw new MediaToolException(
                "processing.probe_failed",
                string.Empty,
                isRetryable: false);
        }

        MediaToolLocation location = RequireLocation();
        try
        {
            CapturedProcess result = await RunCapturedAsync(
                location.FfprobePath,
                [
                    "-v", "error",
                    "-print_format", "json",
                    "-show_streams",
                    "-show_format",
                    trustedPath
                ],
                ProbeTimeout,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new MediaToolException(
                    "processing.probe_failed",
                    result.DiagnosticTail);
            }

            return FfprobeJsonParser.Parse(result.StandardOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw new MediaToolException(
                "processing.probe_timeout",
                string.Empty);
        }
        catch (MediaToolException)
        {
            throw;
        }
        catch (Exception error) when (error is Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new MediaToolException(
                "processing.probe_failed",
                string.Empty);
        }
    }

    public async Task<FfmpegResult> RunFfmpegAsync(
        FfmpegRequest request,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        MediaToolLocation location = RequireLocation();
        var startInfo = new ProcessStartInfo
        {
            FileName = location.FfmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add("-nostats");
        startInfo.ArgumentList.Add(request.OutputPath);

        using var process = new Process { StartInfo = startInfo };
        var diagnostics = new TailBuffer();
        var parser = new FfmpegProgressParser(request.ExpectedDuration);
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception error) when (error is Win32Exception
            or InvalidOperationException)
        {
            throw new MediaToolException(
                "processing.tool_unavailable",
                string.Empty,
                isRetryable: false);
        }

        if (!started)
        {
            throw new MediaToolException(
                "processing.tool_unavailable",
                string.Empty,
                isRetryable: false);
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => TryKill((Process)state!),
            process);
        Task stdout = ReadProgressAsync(process, parser, progress);
        Task stderr = ReadDiagnosticsAsync(process.StandardError, diagnostics);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForExitAndReadersAsync(process, stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
        {
            throw new MediaToolException(
                "processing.ffmpeg_failed",
                diagnostics.Value);
        }

        return new FfmpegResult(process.ExitCode, diagnostics.Value);
    }

    private MediaToolLocation RequireLocation() =>
        locator.TryResolve()
        ?? throw new MediaToolException(
            "processing.tool_missing",
            string.Empty,
            isRetryable: false);

    private static async Task<CapturedProcess> RunCapturedAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The media tool did not start.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using CancellationTokenRegistration registration = timeoutSource.Token.Register(
            static state => TryKill((Process)state!),
            process);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        var diagnostics = new TailBuffer();
        Task stderr = ReadDiagnosticsAsync(process.StandardError, diagnostics);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForExitAndReadersAsync(process, stdout, stderr);
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new TimeoutException("The media tool timed out.");
        }

        await Task.WhenAll(stdout, stderr);
        return new CapturedProcess(
            process.ExitCode,
            await stdout,
            diagnostics.Value);
    }

    private static async Task ReadProgressAsync(
        Process process,
        FfmpegProgressParser parser,
        IProgress<int> progress)
    {
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            int? update = parser.Accept(line);
            if (update.HasValue)
            {
                progress.Report(update.Value);
            }
        }
    }

    private static async Task ReadDiagnosticsAsync(
        StreamReader reader,
        TailBuffer diagnostics)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            diagnostics.AppendLine(line);
        }
    }

    private static async Task WaitForExitAndReadersAsync(
        Process process,
        Task stdout,
        Task stderr)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The exact process instance failed before it fully started.
        }

        await Task.WhenAll(stdout, stderr);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            // Exit races are expected while cancellation is being observed.
        }
    }

    private sealed record CapturedProcess(
        int ExitCode,
        string StandardOutput,
        string DiagnosticTail);

    private sealed class TailBuffer
    {
        private const int MaximumCharacters = 32 * 1024;
        private readonly StringBuilder value = new(MaximumCharacters);

        public string Value => value.ToString();

        public void AppendLine(string line)
        {
            value.AppendLine(line);
            if (value.Length > MaximumCharacters)
            {
                value.Remove(0, value.Length - MaximumCharacters);
            }
        }
    }
}
