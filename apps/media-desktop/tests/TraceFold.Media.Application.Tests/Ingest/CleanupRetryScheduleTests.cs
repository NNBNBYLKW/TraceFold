using TraceFold.Media.Application.Ingest;
using Xunit;

namespace TraceFold.Media.Application.Tests.Ingest;

public sealed class CleanupRetryScheduleTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 3600)]
    public void Failure_count_maps_to_exact_delay(int count, int seconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(seconds),
            CleanupRetrySchedule.DelayAfterFailure(count));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Fifth_and_later_failure_stop_the_current_automatic_round(int count)
    {
        Assert.Null(CleanupRetrySchedule.DelayAfterFailure(count));
    }

    [Fact]
    public void Nonpositive_failure_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CleanupRetrySchedule.DelayAfterFailure(0));
    }
}
