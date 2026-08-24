namespace TraceFold.Media.Application.Ingest;

public static class CleanupRetrySchedule
{
    public static TimeSpan? DelayAfterFailure(int failureCount) => failureCount switch
    {
        <= 0 => throw new ArgumentOutOfRangeException(nameof(failureCount)),
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromHours(1),
        _ => null
    };
}
