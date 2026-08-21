namespace TraceFold.Media.Application.Intake;

public enum ScanOutcome
{
    Completed,
    NotConfigured,
    InvalidConfiguration,
    Failed
}

public sealed record ScanResult(
    ScanOutcome Outcome,
    int ObservedFiles,
    int CreatedItems,
    int UpdatedItems,
    DateTimeOffset CompletedAtUtc,
    string Message);

public interface IInboxScanner
{
    Task<ScanResult> ScanAsync(
        ScanTrigger trigger,
        CancellationToken cancellationToken);
}
