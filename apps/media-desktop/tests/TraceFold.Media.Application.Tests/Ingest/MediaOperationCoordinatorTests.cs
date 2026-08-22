using TraceFold.Media.Application.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class MediaOperationCoordinatorTests
{
    [Fact]
    public async Task Stop_rejects_new_operations_and_waits_for_active_lease()
    {
        var coordinator = new MediaOperationCoordinator();
        await using IMediaOperationLease lease = coordinator.Begin(
            TestContext.Current.CancellationToken);

        Task drain = coordinator.StopAcceptingAndDrainAsync(
            TestContext.Current.CancellationToken);
        Assert.False(drain.IsCompleted);
        Assert.Throws<MediaOperationRejectedException>(() =>
            coordinator.Begin(TestContext.Current.CancellationToken));

        await lease.DisposeAsync();
        await drain;
    }

    [Fact]
    public async Task Disposing_a_lease_twice_decrements_active_count_once()
    {
        var coordinator = new MediaOperationCoordinator();
        IMediaOperationLease lease = coordinator.Begin(
            TestContext.Current.CancellationToken);
        await lease.DisposeAsync();
        await lease.DisposeAsync();

        await coordinator.StopAcceptingAndDrainAsync(
            TestContext.Current.CancellationToken);
    }
}
