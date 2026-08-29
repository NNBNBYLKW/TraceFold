using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed record TaxonomyTermSnapshot(
    Guid Id,
    TaxonomyKind Kind,
    string DisplayName,
    string NormalizedName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaxonomyNameOwnerSnapshot(
    string NormalizedName,
    Guid TermId);

public sealed record TaxonomyAssignmentSnapshot(
    IReadOnlyList<Guid> AuthorIds,
    Guid? SeriesId,
    IReadOnlyList<Guid> TagIds);

public sealed record TaxonomyOwnerSnapshot(
    MetadataOwner Owner,
    string VisibleName,
    long Version,
    TaxonomyAssignmentSnapshot Assignment);

public sealed record TaxonomyMergeSelection(
    TaxonomyKind Kind,
    IReadOnlyList<Guid> SourceIds,
    Guid TargetId)
{
    public static TaxonomyMergeSelection Create(
        TaxonomyKind kind,
        IEnumerable<Guid> sourceIds,
        Guid targetId)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (!Enum.IsDefined(kind) || targetId == Guid.Empty)
        {
            throw new ArgumentException("The merge target is invalid.");
        }

        Guid[] copied = sourceIds.ToArray();
        if (copied.Length == 0
            || copied.Any(id => id == Guid.Empty)
            || copied.Distinct().Count() != copied.Length
            || copied.Contains(targetId))
        {
            throw new ArgumentException("The merge sources are invalid.");
        }

        return new TaxonomyMergeSelection(
            kind,
            Array.AsReadOnly(copied),
            targetId);
    }
}

public sealed record TaxonomyDeleteSelection(
    TaxonomyKind Kind,
    IReadOnlyList<Guid> TermIds)
{
    public static TaxonomyDeleteSelection Create(
        TaxonomyKind kind,
        IEnumerable<Guid> termIds)
    {
        ArgumentNullException.ThrowIfNull(termIds);
        Guid[] copied = termIds.ToArray();
        if (!Enum.IsDefined(kind)
            || copied.Length == 0
            || copied.Any(id => id == Guid.Empty)
            || copied.Distinct().Count() != copied.Length)
        {
            throw new ArgumentException("The delete selection is invalid.");
        }

        return new TaxonomyDeleteSelection(
            kind,
            Array.AsReadOnly(copied));
    }
}

public sealed record TaxonomyMergePreparation(
    Guid OperationId,
    TaxonomyMergeSelection Selection,
    TaxonomyTermSnapshot Target,
    IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyNameOwnerSnapshot> SameKindNames,
    IReadOnlyList<TaxonomyOwnerSnapshot> Owners);

public sealed record TaxonomyMergePlanOwner(
    MetadataOwner Owner,
    string VisibleName,
    long BeforeVersion,
    TaxonomyAssignmentSnapshot Before,
    TaxonomyAssignmentSnapshot After);

public sealed record TaxonomyMergePlan(
    Guid OperationId,
    TaxonomyKind Kind,
    TaxonomyTermSnapshot Target,
    TaxonomyName FinalName,
    IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyMergePlanOwner> Owners);

public sealed record TaxonomyMergePreviewRowDto(
    MetadataOwner Owner,
    string VisibleName,
    TaxonomyAssignmentSnapshot Before,
    TaxonomyAssignmentSnapshot After);

public sealed record TaxonomyMergePreviewDto(
    IReadOnlyList<TaxonomyMergePreviewRowDto> Rows,
    int InboxAffectedCount,
    int LibraryAffectedCount,
    bool CanApply,
    string? ErrorCode);

public sealed record TaxonomyDeleteBlocker(
    TaxonomyTermSnapshot Term,
    int InboxReferenceCount,
    int LibraryReferenceCount);

public sealed record TaxonomyDeletePreparation(
    Guid OperationId,
    TaxonomyDeleteSelection Selection,
    IReadOnlyList<TaxonomyTermSnapshot> Terms,
    IReadOnlyList<TaxonomyDeleteBlocker> Blockers)
{
    public bool CanDelete => Terms.Count > 0 && Blockers.Count == 0;
}

public abstract record TaxonomyMaintenanceUndoReceipt(Guid OperationId);

public sealed record TaxonomyMergeUndoOwner(
    MetadataOwner Owner,
    long ExpectedPostVersion,
    TaxonomyAssignmentSnapshot Before,
    TaxonomyAssignmentSnapshot After);

public sealed record TaxonomyMergeUndoReceipt(
    Guid OperationId,
    TaxonomyTermSnapshot TargetBefore,
    TaxonomyName TargetAfter,
    IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyMergeUndoOwner> Owners)
    : TaxonomyMaintenanceUndoReceipt(OperationId);

public sealed record TaxonomyDeleteUndoReceipt(
    Guid OperationId,
    IReadOnlyList<TaxonomyTermSnapshot> Terms)
    : TaxonomyMaintenanceUndoReceipt(OperationId);

public sealed record TaxonomyMergePrepareResult(
    bool Succeeded,
    TaxonomyMergePreparation? Preparation,
    string? ErrorCode);

public sealed record TaxonomyDeletePrepareResult(
    bool Succeeded,
    TaxonomyDeletePreparation? Preparation,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceStoreResult(
    bool Succeeded,
    IReadOnlyList<MetadataOwner> ChangedOwners,
    int DeletedTermCount,
    TaxonomyMaintenanceUndoReceipt? UndoReceipt,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceResult(
    bool Succeeded,
    int ChangedInboxCount,
    int ChangedLibraryCount,
    int DeletedTermCount,
    bool CanUndo,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceUndoState(
    bool IsAvailable,
    int AffectedOwnerCount,
    int AffectedTermCount)
{
    public static TaxonomyMaintenanceUndoState Unavailable { get; } =
        new(false, 0, 0);
}

public sealed record TaxonomyMaintenanceUndoResult(
    bool Succeeded,
    int RestoredOwnerCount,
    int RestoredTermCount,
    IReadOnlyList<MetadataOwner> ChangedOwners,
    bool ReceiptIsRetryable,
    string? ErrorCode);

public static class TaxonomyMaintenanceErrorCodes
{
    public const string SelectionInvalid =
        "taxonomy.maintenance.selection_invalid";
    public const string NotFound = "taxonomy.maintenance.not_found";
    public const string KindMismatch = "taxonomy.maintenance.kind_mismatch";
    public const string TargetInvalid = "taxonomy.maintenance.target_invalid";
    public const string NameInvalid = "taxonomy.maintenance.name_invalid";
    public const string NameConflict = "taxonomy.maintenance.name_conflict";
    public const string PreviewStale = "taxonomy.maintenance.preview_stale";
    public const string DeleteInUse = "taxonomy.maintenance.delete.in_use";
    public const string PersistenceFailed =
        "taxonomy.maintenance.persistence_failed";
    public const string UndoUnavailable =
        "taxonomy.maintenance.undo.unavailable";
    public const string UndoConflict = "taxonomy.maintenance.undo.conflict";
}

public interface ITaxonomyMaintenanceStore
{
    Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection,
        CancellationToken cancellationToken);

    Task<TaxonomyMaintenanceStoreResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation,
        TaxonomyName? finalName,
        CancellationToken cancellationToken);

    Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection,
        CancellationToken cancellationToken);

    Task<TaxonomyMaintenanceStoreResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken);

    Task<TaxonomyMaintenanceUndoResult> UndoAsync(
        TaxonomyMaintenanceUndoReceipt receipt,
        CancellationToken cancellationToken);
}

public interface ITaxonomyMaintenanceService
{
    TaxonomyMaintenanceUndoState UndoState { get; }

    event EventHandler? UndoStateChanged;

    Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection,
        CancellationToken cancellationToken);

    TaxonomyMergePreviewDto PreviewMerge(
        TaxonomyMergePreparation preparation,
        string? finalNameOverride);

    Task<TaxonomyMaintenanceResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation,
        string? finalNameOverride,
        CancellationToken cancellationToken);

    Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection,
        CancellationToken cancellationToken);

    Task<TaxonomyMaintenanceResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken);

    Task<TaxonomyMaintenanceUndoResult> UndoLastAsync(
        CancellationToken cancellationToken);

    void InvalidateUndo();
}
