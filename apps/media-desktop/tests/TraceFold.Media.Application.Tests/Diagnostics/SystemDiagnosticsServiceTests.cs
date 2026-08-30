using System.Diagnostics;
using TraceFold.Media.Application.Diagnostics;
using Xunit;

namespace TraceFold.Media.Application.Tests.Diagnostics;

public sealed class SystemDiagnosticsServiceTests
{
    [Fact]
    public async Task Check_keeps_successes_when_one_probe_fails_and_orders_results()
    {
        var service = Service(
            SuccessfulProbe(Item(
                "runtime.status",
                SystemDiagnosticCategory.RuntimeAndLogs,
                SystemDiagnosticSeverity.Healthy,
                order: 1)),
            Probe(
                Descriptor("database", SystemDiagnosticCategory.Database),
                _ => throw new InvalidOperationException("private")),
            Probe(
                Descriptor("folders", SystemDiagnosticCategory.FoldersAndStorage),
                _ => Task.FromResult<IReadOnlyList<SystemDiagnosticItem>>
                ([
                    Item(
                        "folders.attention",
                        SystemDiagnosticCategory.FoldersAndStorage,
                        SystemDiagnosticSeverity.Attention,
                        order: 2),
                    Item(
                        "folders.unavailable",
                        SystemDiagnosticCategory.FoldersAndStorage,
                        SystemDiagnosticSeverity.Unavailable,
                        order: 3),
                    Item(
                        "folders.healthy",
                        SystemDiagnosticCategory.FoldersAndStorage,
                        SystemDiagnosticSeverity.Healthy,
                        order: 1)
                ])));

        SystemDiagnosticsSnapshot result = await service.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(SystemDiagnosticSeverity.Unavailable, result.OverallSeverity);
        Assert.Equal(
            new[]
            {
                "folders.unavailable",
                "folders.attention",
                "folders.healthy",
                "database.failure",
                "runtime.status"
            },
            result.Items.Select(item => item.Id));
        Assert.Equal(
            "diagnostics.probe.failed",
            result.Items.Single(item => item.Id == "database.failure").ErrorCode);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task Check_starts_all_probes_before_waiting_for_completion()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ISystemDiagnosticProbe first = GatedProbe(
            "folders",
            SystemDiagnosticCategory.FoldersAndStorage,
            firstStarted,
            release);
        ISystemDiagnosticProbe second = GatedProbe(
            "database",
            SystemDiagnosticCategory.Database,
            secondStarted,
            release);
        SystemDiagnosticsService service = Service(first, second);

        Task<SystemDiagnosticsSnapshot> run = service.CheckAsync(
            TestContext.Current.CancellationToken);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted);
        release.SetResult();
        SystemDiagnosticsSnapshot result = await run;
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Database_timeout_uses_the_probe_specific_code_and_cancels_it()
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ISystemDiagnosticProbe probe = Probe(
            Descriptor(
                "database",
                SystemDiagnosticCategory.Database,
                timeoutErrorCode: "diagnostics.database.timeout"),
            async token =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return [];
                }
                finally
                {
                    if (token.IsCancellationRequested)
                    {
                        cancelled.TrySetResult();
                    }
                }
            });
        var options = new SystemDiagnosticsOptions(
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(150));
        SystemDiagnosticsService service = Service(options, probe);

        SystemDiagnosticsSnapshot result = await service.CheckAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "diagnostics.database.timeout",
            Assert.Single(result.Items).ErrorCode);
        await cancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Caller_cancellation_throws_without_publishing_a_snapshot()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ISystemDiagnosticProbe probe = Probe(
            Descriptor("folders", SystemDiagnosticCategory.FoldersAndStorage),
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return [];
            });
        SystemDiagnosticsService service = Service(probe);
        using var cancellation = new CancellationTokenSource();

        Task<SystemDiagnosticsSnapshot> run = service.CheckAsync(cancellation.Token);
        await started.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Theory]
    [InlineData(InvalidProbeResult.Empty)]
    [InlineData(InvalidProbeResult.WrongCategory)]
    [InlineData(InvalidProbeResult.WrongNamespace)]
    public async Task Invalid_probe_result_becomes_one_stable_failure(
        InvalidProbeResult invalid)
    {
        ISystemDiagnosticProbe probe = Probe(
            Descriptor("database", SystemDiagnosticCategory.Database),
            _ => Task.FromResult(invalid switch
            {
                InvalidProbeResult.Empty =>
                    (IReadOnlyList<SystemDiagnosticItem>)[],
                InvalidProbeResult.WrongCategory =>
                    [Item(
                        "database.status",
                        SystemDiagnosticCategory.BackgroundWork,
                        SystemDiagnosticSeverity.Healthy,
                        order: 0)],
                InvalidProbeResult.WrongNamespace =>
                    [Item(
                        "other.status",
                        SystemDiagnosticCategory.Database,
                        SystemDiagnosticSeverity.Healthy,
                        order: 0)],
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            }));
        SystemDiagnosticsService service = Service(probe);

        SystemDiagnosticsSnapshot result = await service.CheckAsync(
            TestContext.Current.CancellationToken);

        SystemDiagnosticItem item = Assert.Single(result.Items);
        Assert.Equal("database.failure", item.Id);
        Assert.Equal("diagnostics.probe.failed", item.ErrorCode);
    }

    [Fact]
    public async Task Never_completing_probe_cannot_extend_its_timeout_budget()
    {
        ISystemDiagnosticProbe probe = Probe(
            Descriptor("folders", SystemDiagnosticCategory.FoldersAndStorage),
            _ => new TaskCompletionSource<IReadOnlyList<SystemDiagnosticItem>>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task);
        var options = new SystemDiagnosticsOptions(
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(150));
        SystemDiagnosticsService service = Service(options, probe);
        var stopwatch = Stopwatch.StartNew();

        SystemDiagnosticsSnapshot result = await service.CheckAsync(
            TestContext.Current.CancellationToken);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(
            "diagnostics.probe.timeout",
            Assert.Single(result.Items).ErrorCode);
    }

    [Fact]
    public void Constructor_rejects_duplicate_probe_ids()
    {
        ISystemDiagnosticProbe first = SuccessfulProbe(Item(
            "database.first",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Healthy,
            order: 0));
        ISystemDiagnosticProbe second = SuccessfulProbe(Item(
            "database.second",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Healthy,
            order: 1));

        Assert.Throws<ArgumentException>(() => new SystemDiagnosticsService(
            [first, second],
            SystemDiagnosticsOptions.Production,
            TimeProvider.System));
    }

    internal static SystemDiagnosticsService Service(
        params ISystemDiagnosticProbe[] probes) =>
        Service(SystemDiagnosticsOptions.Production, probes);

    internal static SystemDiagnosticsService Service(
        SystemDiagnosticsOptions options,
        params ISystemDiagnosticProbe[] probes) =>
        new(probes, options, new FixedTimeProvider());

    internal static ISystemDiagnosticProbe SuccessfulProbe(
        SystemDiagnosticItem item)
    {
        string descriptorId = item.Id.Split('.')[0];
        return Probe(
            Descriptor(descriptorId, item.Category),
            _ => Task.FromResult<IReadOnlyList<SystemDiagnosticItem>>([item]));
    }

    internal static ISystemDiagnosticProbe Probe(
        SystemDiagnosticProbeDescriptor descriptor,
        Func<CancellationToken, Task<IReadOnlyList<SystemDiagnosticItem>>> run) =>
        new DelegateProbe(descriptor, run);

    internal static SystemDiagnosticProbeDescriptor Descriptor(
        string id,
        SystemDiagnosticCategory category,
        string timeoutErrorCode = "diagnostics.probe.timeout") =>
        SystemDiagnosticProbeDescriptor.Create(
            id,
            category,
            order: (int)category,
            failureSeverity: SystemDiagnosticSeverity.Unavailable,
            failureTitle: "诊断失败",
            failureConclusion: "无法完成检查",
            failureImpact: "当前状态未知。",
            failureGuidance: "请重新检查或查看日志。",
            timeoutErrorCode);

    internal static SystemDiagnosticItem Item(
        string id,
        SystemDiagnosticCategory category,
        SystemDiagnosticSeverity severity,
        int order) =>
        SystemDiagnosticItem.Create(
            id,
            category,
            severity,
            order,
            title: id,
            conclusion: severity == SystemDiagnosticSeverity.Healthy
                ? "正常"
                : "存在问题",
            impact: severity == SystemDiagnosticSeverity.Healthy
                ? null
                : "功能可能受影响。",
            guidance: severity == SystemDiagnosticSeverity.Healthy
                ? null
                : "请检查。",
            errorCode: severity == SystemDiagnosticSeverity.Healthy
                ? null
                : $"diagnostics.{id}.issue",
            facts: [],
            actions: []);

    private static ISystemDiagnosticProbe GatedProbe(
        string id,
        SystemDiagnosticCategory category,
        TaskCompletionSource started,
        TaskCompletionSource release) =>
        Probe(
            Descriptor(id, category),
            async token =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return [Item(
                    $"{id}.status",
                    category,
                    SystemDiagnosticSeverity.Healthy,
                    order: 0)];
            });

    internal sealed class DelegateProbe(
        SystemDiagnosticProbeDescriptor descriptor,
        Func<CancellationToken, Task<IReadOnlyList<SystemDiagnosticItem>>> run)
        : ISystemDiagnosticProbe
    {
        public SystemDiagnosticProbeDescriptor Descriptor { get; } = descriptor;

        public Task<IReadOnlyList<SystemDiagnosticItem>> CheckAsync(
            CancellationToken cancellationToken) => run(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now =
            DateTimeOffset.Parse("2026-08-30T08:00:00Z");

        public override DateTimeOffset GetUtcNow() => _now;
    }

    public enum InvalidProbeResult
    {
        Empty,
        WrongCategory,
        WrongNamespace
    }
}
