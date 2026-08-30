using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Application.Diagnostics;

public sealed class SystemDiagnosticsService : ISystemDiagnosticsService
{
    private readonly IReadOnlyList<ISystemDiagnosticProbe> _probes;
    private readonly SystemDiagnosticsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SystemDiagnosticsService>? _logger;

    public SystemDiagnosticsService(
        IEnumerable<ISystemDiagnosticProbe> probes,
        SystemDiagnosticsOptions options,
        TimeProvider timeProvider,
        ILogger<SystemDiagnosticsService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ISystemDiagnosticProbe[] probeCopy = [.. probes];
        if (probeCopy.Length == 0 || probeCopy.Any(probe => probe is null))
        {
            throw new ArgumentException(
                "At least one diagnostic probe is required.",
                nameof(probes));
        }

        if (probeCopy.Select(probe => probe.Descriptor.Id)
            .Distinct(StringComparer.Ordinal).Count() != probeCopy.Length)
        {
            throw new ArgumentException(
                "Diagnostic probe IDs must be unique.",
                nameof(probes));
        }

        _probes = Array.AsReadOnly(probeCopy);
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SystemDiagnosticsSnapshot> CheckAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid runId = Guid.NewGuid();
        DateTimeOffset startedAtUtc = _timeProvider.GetUtcNow();
        _logger?.LogInformation(
            "SystemDiagnosticsRunStarted RunId {RunId} ProbeCount {ProbeCount}",
            runId,
            _probes.Count);

        using var wholeRunSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        using ITimer wholeRunTimer = _timeProvider.CreateTimer(
            static state => ((CancellationTokenSource)state!).Cancel(),
            wholeRunSource,
            _options.WholeRunTimeout,
            Timeout.InfiniteTimeSpan);
        Task<IReadOnlyList<SystemDiagnosticItem>>[] probeTasks =
        [
            .. _probes.Select(probe => RunProbeAsync(
                runId,
                probe,
                wholeRunSource.Token,
                cancellationToken))
        ];

        IReadOnlyList<SystemDiagnosticItem>[] probeResults;
        try
        {
            probeResults = await Task.WhenAll(probeTasks);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation(
                "SystemDiagnosticsRunCancelled RunId {RunId}",
                runId);
            throw;
        }

        SystemDiagnosticItem[] items =
        [
            .. probeResults
                .SelectMany(result => result)
                .OrderBy(item => item.Category)
                .ThenByDescending(item => item.Severity)
                .ThenBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
        ];
        DateTimeOffset completedAtUtc = _timeProvider.GetUtcNow();
        SystemDiagnosticSeverity overallSeverity =
            items.Max(item => item.Severity);
        _logger?.LogInformation(
            "SystemDiagnosticsRunCompleted RunId {RunId} ItemCount {ItemCount} Severity {Severity}",
            runId,
            items.Length,
            overallSeverity);
        return SystemDiagnosticsSnapshot.Create(
            runId,
            startedAtUtc,
            completedAtUtc,
            overallSeverity,
            items);
    }

    private async Task<IReadOnlyList<SystemDiagnosticItem>> RunProbeAsync(
        Guid runId,
        ISystemDiagnosticProbe probe,
        CancellationToken wholeRunToken,
        CancellationToken callerToken)
    {
        SystemDiagnosticProbeDescriptor descriptor = probe.Descriptor;
        long startedTimestamp = _timeProvider.GetTimestamp();
        _logger?.LogInformation(
            "SystemDiagnosticProbeStarted RunId {RunId} ProbeId {ProbeId}",
            runId,
            descriptor.Id);
        using var perProbeSource = CancellationTokenSource.CreateLinkedTokenSource(
            wholeRunToken);
        Task<IReadOnlyList<SystemDiagnosticItem>>? checkTask = null;
        try
        {
            checkTask = probe.CheckAsync(perProbeSource.Token)
                ?? throw new InvalidOperationException(
                    "The diagnostic probe returned a null task.");
            TimeSpan timeout = _options.GetProbeTimeout(descriptor.Category);
            IReadOnlyList<SystemDiagnosticItem> result = await checkTask.WaitAsync(
                timeout,
                _timeProvider,
                wholeRunToken);
            if (!IsValidResult(descriptor, result))
            {
                long invalidDuration = DurationMilliseconds(startedTimestamp);
                _logger?.LogWarning(
                    "SystemDiagnosticProbeFailed RunId {RunId} ProbeId {ProbeId} Phase {Phase} DurationMs {DurationMs} ErrorCode {ErrorCode}",
                    runId,
                    descriptor.Id,
                    "invalid_result",
                    invalidDuration,
                    "diagnostics.probe.failed");
                return [CreateFailure(descriptor, "diagnostics.probe.failed")];
            }

            _logger?.LogInformation(
                "SystemDiagnosticProbeCompleted RunId {RunId} ProbeId {ProbeId} DurationMs {DurationMs} ResultCount {ResultCount} Severity {Severity}",
                runId,
                descriptor.Id,
                DurationMilliseconds(startedTimestamp),
                result.Count,
                result.Max(item => item.Severity));
            return result;
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            perProbeSource.Cancel();
            ObserveLateCompletion(checkTask);
            throw;
        }
        catch (TimeoutException)
        {
            perProbeSource.Cancel();
            ObserveLateCompletion(checkTask);
            _logger?.LogWarning(
                "SystemDiagnosticProbeTimedOut RunId {RunId} ProbeId {ProbeId} DurationMs {DurationMs} ErrorCode {ErrorCode}",
                runId,
                descriptor.Id,
                DurationMilliseconds(startedTimestamp),
                descriptor.TimeoutErrorCode);
            return [CreateFailure(descriptor, descriptor.TimeoutErrorCode)];
        }
        catch (OperationCanceledException)
        {
            perProbeSource.Cancel();
            ObserveLateCompletion(checkTask);
            _logger?.LogWarning(
                "SystemDiagnosticProbeTimedOut RunId {RunId} ProbeId {ProbeId} DurationMs {DurationMs} ErrorCode {ErrorCode}",
                runId,
                descriptor.Id,
                DurationMilliseconds(startedTimestamp),
                descriptor.TimeoutErrorCode);
            return [CreateFailure(descriptor, descriptor.TimeoutErrorCode)];
        }
        catch (Exception error)
        {
            ObserveLateCompletion(checkTask);
            _logger?.LogWarning(
                "SystemDiagnosticProbeFailed RunId {RunId} ProbeId {ProbeId} Phase {Phase} DurationMs {DurationMs} ExceptionType {ExceptionType} ErrorCode {ErrorCode}",
                runId,
                descriptor.Id,
                "check",
                DurationMilliseconds(startedTimestamp),
                error.GetType().FullName,
                "diagnostics.probe.failed");
            return [CreateFailure(descriptor, "diagnostics.probe.failed")];
        }
    }

    private static bool IsValidResult(
        SystemDiagnosticProbeDescriptor descriptor,
        IReadOnlyList<SystemDiagnosticItem>? result)
    {
        if (result is null || result.Count == 0 || result.Any(item => item is null))
        {
            return false;
        }

        string prefix = $"{descriptor.Id}.";
        return result.All(item =>
                item.Category == descriptor.Category
                && item.Id.StartsWith(prefix, StringComparison.Ordinal))
            && result.Select(item => item.Id)
                .Distinct(StringComparer.Ordinal).Count() == result.Count;
    }

    private static SystemDiagnosticItem CreateFailure(
        SystemDiagnosticProbeDescriptor descriptor,
        string errorCode) =>
        SystemDiagnosticItem.Create(
            $"{descriptor.Id}.failure",
            descriptor.Category,
            descriptor.FailureSeverity,
            descriptor.Order,
            descriptor.FailureTitle,
            descriptor.FailureConclusion,
            descriptor.FailureImpact,
            descriptor.FailureGuidance,
            errorCode,
            facts: [],
            actions:
            [
                SystemDiagnosticAction.Create(
                    SystemDiagnosticActionKind.CopyErrorCode,
                    SystemDiagnosticActionTarget.None,
                    "复制错误代码")
            ]);

    private long DurationMilliseconds(long startedTimestamp) =>
        Math.Max(
            0,
            (long)_timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    private static void ObserveLateCompletion(Task? task)
    {
        if (task is null || task.IsCompletedSuccessfully || task.IsCanceled)
        {
            return;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
