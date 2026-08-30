using System.Text.Json;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Infrastructure.Diagnostics;
using TraceFold.Media.Infrastructure.Persistence;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Diagnostics;

public sealed class BackgroundWorkDiagnosticProbeTests
{
    [Fact]
    public async Task Store_counts_only_the_five_approved_buckets()
    {
        await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
        await database.MigrateAsync();
        await database.SeedBackgroundWorkFixtureAsync(
            queued: 2,
            running: 1,
            interrupted: 3,
            failed: 4,
            recoveryBlocked: 2,
            cleanupPending: 1,
            completedProcessing: 5,
            cancelledProcessing: 2,
            completedIngest: 5,
            failedIngest: 2);
        var store = new EfBackgroundWorkSummaryStore(
            database.CreateContextFactory());

        BackgroundWorkSummary result = await store.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(new BackgroundWorkSummary(2, 1, 3, 4, 3), result);
        SystemDiagnosticItem item = Assert.Single(await
            new BackgroundWorkDiagnosticProbe(store).CheckAsync(
                TestContext.Current.CancellationToken));
        string serialized = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("private-video-", serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("T:\\private\\inbox", serialized,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, 2, 0, 0, 0, SystemDiagnosticSeverity.Healthy)]
    [InlineData(0, 0, 1, 0, 0, SystemDiagnosticSeverity.Attention)]
    [InlineData(0, 0, 0, 1, 0, SystemDiagnosticSeverity.Attention)]
    [InlineData(0, 0, 0, 0, 1, SystemDiagnosticSeverity.Attention)]
    [InlineData(1, 1, 1, 1, 1, SystemDiagnosticSeverity.Attention)]
    public async Task Probe_maps_counts_to_severity(
        int queued,
        int running,
        int interrupted,
        int failed,
        int recovery,
        SystemDiagnosticSeverity expected)
    {
        var store = new FixedBackgroundWorkStore(new BackgroundWorkSummary(
            queued,
            running,
            interrupted,
            failed,
            recovery));

        SystemDiagnosticItem item = Assert.Single(await
            new BackgroundWorkDiagnosticProbe(store).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(expected, item.Severity);
        Assert.Equal(expected == SystemDiagnosticSeverity.Healthy
            ? null
            : "diagnostics.background.attention", item.ErrorCode);
    }

    [Fact]
    public async Task Probe_exposes_only_numeric_shareable_counts()
    {
        var summary = new BackgroundWorkSummary(12, 3, 4, 5, 6);

        SystemDiagnosticItem item = Assert.Single(await
            new BackgroundWorkDiagnosticProbe(new FixedBackgroundWorkStore(summary))
                .CheckAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            new[]
            {
                "background.processing.queued",
                "background.processing.running",
                "background.processing.interrupted",
                "background.processing.failed",
                "background.ingest.recovery_pending"
            },
            item.Facts.Select(fact => fact.Key));
        Assert.All(item.Facts, fact =>
        {
            Assert.Equal(SystemDiagnosticFactSensitivity.Shareable,
                fact.Sensitivity);
            Assert.True(int.TryParse(fact.Value, out _));
        });
        string serialized = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("private-video.mp4", serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("T:\\private\\inbox", serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("56F985A8-03E4-49AA-9D94-690137B2A700", serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Probe_maps_store_failure_without_disclosing_its_message()
    {
        var store = new FailingBackgroundWorkStore(
            new InvalidOperationException(
                "private-video.mp4 T:\\private\\inbox"));

        SystemDiagnosticItem item = Assert.Single(await
            new BackgroundWorkDiagnosticProbe(store).CheckAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
        Assert.Equal("diagnostics.background.query_failed", item.ErrorCode);
        string serialized = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("private-video.mp4", serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("T:\\private\\inbox", serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Probe_propagates_cancellation_for_the_orchestrator()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new FailingBackgroundWorkStore(
            new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new BackgroundWorkDiagnosticProbe(store)
                .CheckAsync(cancellation.Token));
    }

    private sealed class FixedBackgroundWorkStore(BackgroundWorkSummary summary)
        : IBackgroundWorkSummaryStore
    {
        public Task<BackgroundWorkSummary> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(summary);
    }

    private sealed class FailingBackgroundWorkStore(Exception failure)
        : IBackgroundWorkSummaryStore
    {
        public Task<BackgroundWorkSummary> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<BackgroundWorkSummary>(failure);
    }
}
