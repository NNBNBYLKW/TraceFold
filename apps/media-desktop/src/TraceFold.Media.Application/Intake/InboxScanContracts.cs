namespace TraceFold.Media.Application.Intake;

public sealed record ScanReconciliation(
    string InboxDirectory,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<CandidateGroup> Groups,
    IReadOnlySet<string> ObservedPaths);

public sealed record ScanPersistenceResult(
    int CreatedItems,
    int UpdatedItems,
    bool HasWaitingCandidates,
    IReadOnlyList<Guid> SourceChangedItemIds);

public interface IInboxScanStore
{
    Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadFirstObservedAsync(
        IReadOnlySet<string> normalizedPaths,
        CancellationToken cancellationToken);

    Task<ScanPersistenceResult> ReconcileAsync(
        ScanReconciliation reconciliation,
        CancellationToken cancellationToken);

    Task<bool> HasWaitingCandidatesAsync(CancellationToken cancellationToken);

    Task ClearMissingAsync(Guid inboxItemId, CancellationToken cancellationToken);
}
