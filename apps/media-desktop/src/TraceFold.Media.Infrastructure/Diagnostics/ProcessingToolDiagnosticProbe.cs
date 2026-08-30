using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Infrastructure.MediaTools;

namespace TraceFold.Media.Infrastructure.Diagnostics;

public sealed record ControlledProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

public interface IControlledProcessRunner
{
    Task<ControlledProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class ControlledProcessRunner(Action<int>? processStarted = null)
    : IControlledProcessRunner
{
    private const int MaximumCapturedCharacters = 8 * 1024;

    public async Task<ControlledProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executable)
            || !Path.IsPathFullyQualified(executable))
        {
            throw new ArgumentException(
                "The executable path must be fully qualified.",
                nameof(executable));
        }

        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
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
            throw new InvalidOperationException("The controlled process did not start.");
        }

        try
        {
            processStarted?.Invoke(process.Id);
        }
        catch
        {
            TryKill(process);
            await WaitForExitSafelyAsync(process);
            throw;
        }

        Task<string> stdout = ReadBoundedAsync(process.StandardOutput);
        Task<string> stderr = ReadBoundedAsync(process.StandardError);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using CancellationTokenRegistration registration = timeoutSource.Token.Register(
            static state => TryKill((Process)state!),
            process);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForExitAndReadersSafelyAsync(process, stdout, stderr);
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new ControlledProcessResult(
                ExitCode: null,
                await CompletedValueAsync(stdout),
                await CompletedValueAsync(stderr),
                TimedOut: true);
        }

        await Task.WhenAll(stdout, stderr);
        return new ControlledProcessResult(
            process.ExitCode,
            await stdout,
            await stderr,
            TimedOut: false);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var captured = new StringBuilder(MaximumCapturedCharacters);
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            int remaining = MaximumCapturedCharacters - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer, 0, Math.Min(remaining, read));
            }
        }

        return captured.ToString();
    }

    private static async Task WaitForExitAndReadersSafelyAsync(
        Process process,
        Task<string> stdout,
        Task<string> stderr)
    {
        await WaitForExitSafelyAsync(process);
        try
        {
            await Task.WhenAll(stdout, stderr);
        }
        catch (Exception error) when (error is IOException
            or ObjectDisposedException
            or InvalidOperationException)
        {
            // A killed process may close redirected streams while they are draining.
        }
    }

    private static async Task WaitForExitSafelyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Start and exit can race while a cancellation is being delivered.
        }
    }

    private static async Task<string> CompletedValueAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (Exception error) when (error is IOException
            or ObjectDisposedException
            or InvalidOperationException)
        {
            return string.Empty;
        }
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
}

public sealed record ExecutableVersionRead(
    int? ExitCode,
    string? FirstLine,
    bool TimedOut);

public interface IExecutableVersionReader
{
    Task<ExecutableVersionRead> ReadAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class ControlledExecutableVersionReader(
    IControlledProcessRunner processRunner) : IExecutableVersionReader
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(4);

    public async Task<ExecutableVersionRead> ReadAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!arguments.SequenceEqual(["-version"], StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Executable version checks only accept the fixed -version argument.",
                nameof(arguments));
        }

        ControlledProcessResult result = await processRunner.RunAsync(
            executable,
            arguments,
            VersionTimeout,
            cancellationToken);
        string? firstLine = FirstNonblankLine(result.StandardOutput)
            ?? FirstNonblankLine(result.StandardError);
        return new ExecutableVersionRead(
            result.ExitCode,
            firstLine,
            result.TimedOut);
    }

    private static string? FirstNonblankLine(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
}

public sealed class ProcessingToolDiagnosticProbe(
    BundledMediaToolLocator locator,
    IExecutableVersionReader versionReader) : ISystemDiagnosticProbe
{
    private const string FailureImpact =
        "需要前置处理的音视频暂时无法处理；普通图片和无需前置处理的文件仍可管理。";

    public SystemDiagnosticProbeDescriptor Descriptor { get; } =
        SystemDiagnosticProbeDescriptor.Create(
            "tools",
            SystemDiagnosticCategory.ProcessingTools,
            order: 0,
            failureSeverity: SystemDiagnosticSeverity.Unavailable,
            failureTitle: "前置处理工具检查失败",
            failureConclusion: "无法完成前置处理工具检查",
            failureImpact: FailureImpact,
            failureGuidance: "请重新检查；问题持续时查看本地日志。",
            timeoutErrorCode: "diagnostics.tools.timeout");

    public async Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
        CancellationToken cancellationToken)
    {
        BundledMediaToolInspection inspection = locator.Inspect();
        Task<ToolObservation> ffmpegTask = ObserveAsync(
            "ffmpeg",
            inspection.Ffmpeg,
            cancellationToken);
        Task<ToolObservation> ffprobeTask = ObserveAsync(
            "ffprobe",
            inspection.Ffprobe,
            cancellationToken);
        ToolObservation[] observations = await Task.WhenAll(
            ffmpegTask,
            ffprobeTask);
        ToolObservation ffmpeg = observations[0];
        ToolObservation ffprobe = observations[1];
        List<SystemDiagnosticFact> facts = CreateFacts(ffmpeg, ffprobe);

        (string Code, string Conclusion, string Guidance)? failure =
            SelectFailure(ffmpeg, ffprobe);
        if (failure.HasValue)
        {
            return [SystemDiagnosticItem.Create(
                "tools.preprocess",
                SystemDiagnosticCategory.ProcessingTools,
                SystemDiagnosticSeverity.Unavailable,
                order: 0,
                title: "FFmpeg / FFprobe",
                conclusion: failure.Value.Conclusion,
                impact: FailureImpact,
                guidance: failure.Value.Guidance,
                errorCode: failure.Value.Code,
                facts,
                actions:
                [
                    SystemDiagnosticAction.Create(
                        SystemDiagnosticActionKind.CopyErrorCode,
                        SystemDiagnosticActionTarget.None,
                        "复制错误代码")
                ])];
        }

        return [SystemDiagnosticItem.Create(
            "tools.preprocess",
            SystemDiagnosticCategory.ProcessingTools,
            SystemDiagnosticSeverity.Healthy,
            order: 0,
            title: "FFmpeg / FFprobe",
            conclusion: "两个包内前置处理工具均可用",
            impact: null,
            guidance: null,
            errorCode: null,
            facts,
            actions: [])];
    }

    private async Task<ToolObservation> ObserveAsync(
        string tool,
        BundledMediaToolCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.State != BundledMediaToolCandidateState.Available)
        {
            return new ToolObservation(
                tool,
                candidate.Path,
                ToolObservationState.Missing,
                Version: null);
        }

        try
        {
            ExecutableVersionRead result = await versionReader.ReadAsync(
                candidate.Path!,
                ["-version"],
                cancellationToken);
            if (result.TimedOut)
            {
                return new(tool, candidate.Path,
                    ToolObservationState.TimedOut, Version: null);
            }

            if (result.ExitCode != 0)
            {
                return new(tool, candidate.Path,
                    ToolObservationState.LaunchFailed, Version: null);
            }

            if (!TryNormalizeVersion(tool, result.FirstLine, out string? version))
            {
                return new(tool, candidate.Path,
                    ToolObservationState.VersionUnknown, Version: null);
            }

            return new(tool, candidate.Path,
                ToolObservationState.Available, version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is Win32Exception
            or InvalidOperationException
            or IOException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            return new(tool, candidate.Path,
                ToolObservationState.LaunchFailed, Version: null);
        }
    }

    private static bool TryNormalizeVersion(
        string tool,
        string? firstLine,
        out string? version)
    {
        const int maximumTokenLength = 128;
        string expectedPrefix = $"{tool} version ";
        version = null;
        if (string.IsNullOrWhiteSpace(firstLine)
            || !firstLine.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = firstLine.AsSpan(expectedPrefix.Length);
        int separator = remainder.IndexOfAny(" \t\r\n");
        ReadOnlySpan<char> token = separator >= 0
            ? remainder[..separator]
            : remainder;
        if (token.IsEmpty
            || token.Length > maximumTokenLength
            || !token.ContainsAnyInRange('0', '9'))
        {
            return false;
        }

        foreach (char character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '-' or '_' or '+'))
            {
                return false;
            }
        }

        version = $"{tool} version {token.ToString()}";
        return true;
    }

    private static (string Code, string Conclusion, string Guidance)? SelectFailure(
        ToolObservation ffmpeg,
        ToolObservation ffprobe)
    {
        if (ffmpeg.State == ToolObservationState.Missing)
        {
            return (
                "diagnostics.tools.ffmpeg_missing",
                "包内 FFmpeg 缺失或路径不安全",
                "请重新安装完整测试包或正式版本。");
        }

        if (ffprobe.State == ToolObservationState.Missing)
        {
            return (
                "diagnostics.tools.ffprobe_missing",
                "包内 FFprobe 缺失或路径不安全",
                "请重新安装完整测试包或正式版本。");
        }

        if (ffmpeg.State == ToolObservationState.TimedOut
            || ffprobe.State == ToolObservationState.TimedOut)
        {
            return (
                "diagnostics.tools.timeout",
                "前置处理工具响应超时",
                "请重新检查；问题持续时查看本地日志。");
        }

        if (ffmpeg.State == ToolObservationState.LaunchFailed
            || ffprobe.State == ToolObservationState.LaunchFailed)
        {
            return (
                "diagnostics.tools.launch_failed",
                "前置处理工具无法正常启动",
                "请检查安全软件拦截情况或重新安装。");
        }

        if (ffmpeg.State == ToolObservationState.VersionUnknown
            || ffprobe.State == ToolObservationState.VersionUnknown)
        {
            return (
                "diagnostics.tools.version_unknown",
                "无法识别前置处理工具版本",
                "请重新安装程序自带的工具文件。");
        }

        return null;
    }

    private static List<SystemDiagnosticFact> CreateFacts(
        params ToolObservation[] observations)
    {
        var facts = new List<SystemDiagnosticFact>();
        int order = 0;
        foreach (ToolObservation observation in observations)
        {
            facts.Add(Fact(
                $"tools.{observation.Tool}.available",
                $"{observation.Tool} 可用",
                observation.State == ToolObservationState.Available ? "是" : "否",
                SystemDiagnosticFactSensitivity.Shareable,
                order++));
            if (!string.IsNullOrWhiteSpace(observation.Path))
            {
                facts.Add(Fact(
                    $"tools.{observation.Tool}.path",
                    $"{observation.Tool} 路径",
                    observation.Path,
                    SystemDiagnosticFactSensitivity.LocalOnly,
                    order++));
            }

            if (!string.IsNullOrWhiteSpace(observation.Version))
            {
                facts.Add(Fact(
                    $"tools.{observation.Tool}.version",
                    $"{observation.Tool} 版本",
                    observation.Version,
                    SystemDiagnosticFactSensitivity.Shareable,
                    order++));
            }
        }

        return facts;
    }

    private static SystemDiagnosticFact Fact(
        string key,
        string label,
        string value,
        SystemDiagnosticFactSensitivity sensitivity,
        int order) =>
        SystemDiagnosticFact.Create(key, label, value, sensitivity, order);

    private enum ToolObservationState
    {
        Available,
        Missing,
        LaunchFailed,
        VersionUnknown,
        TimedOut
    }

    private sealed record ToolObservation(
        string Tool,
        string? Path,
        ToolObservationState State,
        string? Version);
}
