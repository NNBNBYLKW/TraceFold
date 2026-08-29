using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Library;

public sealed record LibraryBatchAssetDto(
    LibrarySelectionEntry Selection,
    string ActualFilename,
    string? ExplicitDisplayName,
    IReadOnlyList<TaxonomyTermDto> Authors,
    TaxonomyTermDto? Series,
    IReadOnlyList<TaxonomyTermDto> Tags);

public sealed record LibraryBatchEditorDto(
    IReadOnlyList<LibraryBatchAssetDto> Assets,
    IReadOnlyList<BatchTermCoverageDto> Authors,
    IReadOnlyList<BatchTermCoverageDto> Tags,
    IReadOnlySet<string> UnselectedDisplayNames);

public sealed record LibraryBatchEditDraft(
    BatchMetadataDraft? Metadata,
    NamingDefinition? Naming,
    IReadOnlyList<TaxonomyTermDto> ExistingTermSnapshots)
{
    public static LibraryBatchEditDraft Create(
        BatchMetadataDraft? metadata,
        NamingDefinition? naming,
        IReadOnlyList<TaxonomyTermDto>? existingTermSnapshots = null)
    {
        if (metadata is null && naming is null)
        {
            throw new ArgumentException("At least one batch edit is required.");
        }

        if (metadata is not null)
        {
            BatchMetadataDraft.Validate(metadata);
        }

        TaxonomyTermDto[] snapshots = ValidateExistingTermSnapshots(
            metadata,
            existingTermSnapshots ?? []);
        return new LibraryBatchEditDraft(
            metadata,
            naming,
            Array.AsReadOnly(snapshots));
    }

    private static TaxonomyTermDto[] ValidateExistingTermSnapshots(
        BatchMetadataDraft? metadata,
        IReadOnlyList<TaxonomyTermDto> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var referencedTerms = new Dictionary<Guid, TaxonomyKind>();
        IEnumerable<BatchTaxonomyReference> references = metadata is null
            ? []
            : metadata.Authors.Values
                .Concat(metadata.Tags.Values)
                .Append(metadata.Series.Value)
                .Where(reference => reference is not null)
                .Select(reference => reference!);
        foreach (BatchTaxonomyReference reference in references)
        {
            if (reference.ExistingId is not Guid existingId)
            {
                continue;
            }

            if (referencedTerms.TryGetValue(
                    existingId,
                    out TaxonomyKind existingKind)
                && existingKind != reference.Kind)
            {
                throw new ArgumentException(
                    "The taxonomy reference is inconsistent.",
                    nameof(metadata));
            }

            referencedTerms.TryAdd(existingId, reference.Kind);
        }

        var seen = new HashSet<Guid>();
        foreach (TaxonomyTermDto? term in snapshots)
        {
            if (term is null
                || term.Id == Guid.Empty
                || !Enum.IsDefined(term.Kind)
                || !seen.Add(term.Id)
                || !referencedTerms.TryGetValue(
                    term.Id,
                    out TaxonomyKind referencedKind)
                || referencedKind != term.Kind)
            {
                throw new ArgumentException(
                    "The taxonomy snapshot is invalid.",
                    nameof(snapshots));
            }

            TaxonomyName name = TaxonomyName.Create(term.DisplayName);
            if (!StringComparer.Ordinal.Equals(
                    name.Normalized,
                    term.NormalizedName))
            {
                throw new ArgumentException(
                    "The taxonomy snapshot is invalid.",
                    nameof(snapshots));
            }
        }

        if (seen.Count != referencedTerms.Count)
        {
            throw new ArgumentException(
                "Every existing taxonomy reference requires a snapshot.",
                nameof(snapshots));
        }

        return snapshots.ToArray();
    }
}

public sealed record LibraryBatchPreviewRowDto(
    Guid AssetId,
    int Ordinal,
    string ActualFilename,
    string CurrentDisplayName,
    string? GeneratedDisplayName,
    bool MetadataChanged,
    bool DisplayNameChanged,
    IReadOnlyList<NamingIssue> Issues);

public sealed record LibraryBatchPreviewDto(
    IReadOnlyList<LibraryBatchPreviewRowDto> Rows,
    bool CanApply,
    int ChangedCount,
    int WarningCount,
    int ErrorCount,
    string? ErrorCode);

public interface ILibraryBatchPreviewService
{
    LibraryBatchPreviewDto Preview(
        LibraryBatchEditorDto editor,
        LibraryBatchEditDraft draft);
}

public sealed record LibraryBatchPrepareResult(
    bool Succeeded,
    LibraryBatchEditorDto? Editor,
    string? ErrorCode);

public sealed record LibraryBatchStoreResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int WarningCount,
    LibraryBatchUndoReceipt? UndoReceipt,
    string? ErrorCode);

public sealed record LibraryBatchUndoReceipt(
    Guid OperationId,
    IReadOnlyList<LibraryBatchUndoItem> Items);

public sealed record LibraryBatchUndoItem(
    Guid AssetId,
    long ExpectedPostVersion,
    string? PreviousDisplayName,
    Guid? PreviousSeriesId,
    IReadOnlyList<Guid> PreviousAuthorIds,
    IReadOnlyList<Guid> PreviousTagIds);

public sealed record LibraryBatchApplyResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int WarningCount,
    bool CanUndo,
    string? ErrorCode);

public sealed record LibraryBatchUndoState(
    bool IsAvailable,
    int AffectedCount)
{
    public static LibraryBatchUndoState Unavailable { get; } = new(false, 0);
}

public sealed record LibraryBatchUndoResult(
    bool Succeeded,
    int RestoredCount,
    string? ErrorCode);

public interface ILibraryBatchEditStore
{
    Task<LibraryBatchPrepareResult> PrepareAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<LibraryBatchStoreResult> ApplyAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        LibraryBatchEditDraft draft,
        CancellationToken cancellationToken);

    Task<LibraryBatchUndoResult> UndoAsync(
        LibraryBatchUndoReceipt receipt,
        CancellationToken cancellationToken);
}

public interface ILibraryBatchEditorService
{
    LibraryBatchUndoState UndoState { get; }

    event EventHandler? UndoStateChanged;

    Task<LibraryBatchPrepareResult> PrepareAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken);

    LibraryBatchPreviewDto Preview(
        LibraryBatchEditorDto editor,
        LibraryBatchEditDraft draft);

    Task<LibraryBatchApplyResult> ApplyAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        LibraryBatchEditDraft draft,
        CancellationToken cancellationToken);

    Task<LibraryBatchUndoResult> UndoLastAsync(
        CancellationToken cancellationToken);

    void InvalidateUndoFor(Guid assetId);
}

public enum LibraryActivityKind
{
    SingleMetadataChanged,
    TaxonomyChanged,
    BatchChanged,
    UndoCompleted
}

public sealed class LibraryActivityEventArgs(
    LibraryActivityKind kind,
    IReadOnlyList<Guid> assetIds) : EventArgs
{
    public LibraryActivityKind Kind { get; } = kind;

    public IReadOnlyList<Guid> AssetIds { get; } = assetIds.ToArray();
}

public interface ILibraryActivityNotifier
{
    event EventHandler<LibraryActivityEventArgs>? Changed;

    void Publish(
        LibraryActivityKind kind,
        IReadOnlyList<Guid> assetIds);
}

public sealed class LibraryActivityNotifier : ILibraryActivityNotifier
{
    public event EventHandler<LibraryActivityEventArgs>? Changed;

    public void Publish(
        LibraryActivityKind kind,
        IReadOnlyList<Guid> assetIds)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        Changed?.Invoke(this, new LibraryActivityEventArgs(kind, assetIds));
    }
}
