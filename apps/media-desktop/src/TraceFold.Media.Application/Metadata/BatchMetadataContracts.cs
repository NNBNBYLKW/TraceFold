using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public enum BatchMetadataMode
{
    Add,
    Remove,
    Replace
}

public enum BatchMetadataFieldOperation
{
    Unchanged,
    Add,
    Remove,
    Replace,
    Clear
}

public sealed record BatchTaxonomyReference(
    TaxonomyKind Kind,
    Guid? ExistingId,
    string? NewName)
{
    public static BatchTaxonomyReference Existing(TaxonomyKind kind, Guid id) =>
        id == Guid.Empty
            ? throw new ArgumentException("A persisted taxonomy id is required.")
            : new(kind, id, null);

    public static BatchTaxonomyReference New(TaxonomyKind kind, string name) =>
        new(kind, null, name);
}

public sealed record BatchMetadataSetEdit(
    BatchMetadataFieldOperation Operation,
    IReadOnlyList<BatchTaxonomyReference> Values)
{
    public static BatchMetadataSetEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, []);
}

public sealed record BatchMetadataSeriesEdit(
    BatchMetadataFieldOperation Operation,
    BatchTaxonomyReference? Value)
{
    public static BatchMetadataSeriesEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, null);
}

public sealed record BatchMetadataDraft(
    BatchMetadataMode Mode,
    BatchMetadataSetEdit Authors,
    BatchMetadataSeriesEdit Series,
    BatchMetadataSetEdit Tags)
{
    public static BatchMetadataDraft Create(
        BatchMetadataMode mode,
        BatchMetadataSetEdit authors,
        BatchMetadataSeriesEdit series,
        BatchMetadataSetEdit tags)
    {
        var draft = new BatchMetadataDraft(mode, authors, series, tags);
        Validate(draft);
        return draft;
    }

    internal static void Validate(BatchMetadataDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        BatchMetadataMode mode = draft.Mode;
        BatchMetadataSetEdit authors = draft.Authors;
        BatchMetadataSeriesEdit series = draft.Series;
        BatchMetadataSetEdit tags = draft.Tags;
        ArgumentNullException.ThrowIfNull(authors);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(tags);

        ValidateSet(authors, mode, TaxonomyKind.Author);
        ValidateSeries(series, mode);
        ValidateSet(tags, mode, TaxonomyKind.Tag);
        if (authors.Operation == BatchMetadataFieldOperation.Unchanged
            && series.Operation == BatchMetadataFieldOperation.Unchanged
            && tags.Operation == BatchMetadataFieldOperation.Unchanged)
        {
            throw new ArgumentException("At least one metadata field must change.");
        }
    }

    private static void ValidateSet(
        BatchMetadataSetEdit edit,
        BatchMetadataMode mode,
        TaxonomyKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(edit.Values);
        bool operationAllowed = mode switch
        {
            BatchMetadataMode.Add => edit.Operation is
                BatchMetadataFieldOperation.Unchanged
                or BatchMetadataFieldOperation.Add,
            BatchMetadataMode.Remove => edit.Operation is
                BatchMetadataFieldOperation.Unchanged
                or BatchMetadataFieldOperation.Remove,
            BatchMetadataMode.Replace => edit.Operation is
                BatchMetadataFieldOperation.Unchanged
                or BatchMetadataFieldOperation.Replace
                or BatchMetadataFieldOperation.Clear,
            _ => false
        };
        if (!operationAllowed)
        {
            throw new ArgumentException("The field operation does not match the mode.");
        }

        bool hasValues = edit.Values.Count > 0;
        if (edit.Operation is BatchMetadataFieldOperation.Unchanged
            or BatchMetadataFieldOperation.Clear)
        {
            if (hasValues)
            {
                throw new ArgumentException("This field operation cannot carry values.");
            }

            return;
        }

        if (!hasValues)
        {
            throw new ArgumentException("This field operation requires values.");
        }

        foreach (BatchTaxonomyReference value in edit.Values)
        {
            if (value is null || value.Kind != expectedKind)
            {
                throw new ArgumentException("The taxonomy kind is invalid for this field.");
            }

            if (edit.Operation == BatchMetadataFieldOperation.Remove
                && !value.ExistingId.HasValue)
            {
                throw new ArgumentException("Remove requires persisted taxonomy terms.");
            }
        }
    }

    private static void ValidateSeries(
        BatchMetadataSeriesEdit edit,
        BatchMetadataMode mode)
    {
        if (mode != BatchMetadataMode.Replace)
        {
            if (edit.Operation != BatchMetadataFieldOperation.Unchanged
                || edit.Value is not null)
            {
                throw new ArgumentException("Series can change only in replace mode.");
            }

            return;
        }

        if (edit.Operation is not (
            BatchMetadataFieldOperation.Unchanged
            or BatchMetadataFieldOperation.Replace
            or BatchMetadataFieldOperation.Clear))
        {
            throw new ArgumentException("The series operation is invalid.");
        }

        if (edit.Operation == BatchMetadataFieldOperation.Replace)
        {
            if (edit.Value is null || edit.Value.Kind != TaxonomyKind.Series)
            {
                throw new ArgumentException("Replace requires one series value.");
            }
        }
        else if (edit.Value is not null)
        {
            throw new ArgumentException("This series operation cannot carry a value.");
        }
    }
}

public sealed record BatchMetadataCommand
{
    public BatchMetadataCommand(
        IReadOnlyList<InboxSelectionEntry> selection,
        BatchMetadataMode mode,
        BatchMetadataSetEdit authors,
        BatchMetadataSeriesEdit series,
        BatchMetadataSetEdit tags)
        : this(selection, new BatchMetadataDraft(mode, authors, series, tags))
    {
    }

    private BatchMetadataCommand(
        IReadOnlyList<InboxSelectionEntry> selection,
        BatchMetadataDraft draft)
    {
        Selection = selection;
        Draft = draft;
    }

    public IReadOnlyList<InboxSelectionEntry> Selection { get; }

    public BatchMetadataDraft Draft { get; }

    public BatchMetadataMode Mode => Draft.Mode;

    public BatchMetadataSetEdit Authors => Draft.Authors;

    public BatchMetadataSeriesEdit Series => Draft.Series;

    public BatchMetadataSetEdit Tags => Draft.Tags;

    public static BatchMetadataCommand Create(
        IReadOnlyList<InboxSelectionEntry> selection,
        BatchMetadataMode mode,
        BatchMetadataSetEdit authors,
        BatchMetadataSeriesEdit series,
        BatchMetadataSetEdit tags) =>
        Create(
            selection,
            BatchMetadataDraft.Create(mode, authors, series, tags));

    public static BatchMetadataCommand Create(
        IReadOnlyList<InboxSelectionEntry> selection,
        BatchMetadataDraft draft)
    {
        ValidateSelection(selection);
        BatchMetadataDraft.Validate(draft);
        return new BatchMetadataCommand(selection, draft);
    }

    private static void ValidateSelection(
        IReadOnlyList<InboxSelectionEntry> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Count == 0)
        {
            throw new ArgumentException("At least one inbox item is required.");
        }

        var ids = new HashSet<Guid>();
        var ordinals = new HashSet<int>();
        foreach (InboxSelectionEntry entry in selection)
        {
            if (entry is null
                || entry.InboxItemId == Guid.Empty
                || entry.ExpectedMetadataVersion < 0
                || entry.Ordinal < 0
                || !ids.Add(entry.InboxItemId)
                || !ordinals.Add(entry.Ordinal))
            {
                throw new ArgumentException("The inbox selection is invalid.");
            }
        }
    }
}

public sealed record BatchTermCoverageDto(
    TaxonomyTermDto Term,
    int SelectedCount);

public sealed record BatchMetadataEditorDto(
    int SelectedCount,
    IReadOnlyList<BatchTermCoverageDto> Authors,
    IReadOnlyList<BatchTermCoverageDto> Tags);

public sealed record ResolvedBatchSetEdit(
    BatchMetadataFieldOperation Operation,
    IReadOnlyList<Guid> Values)
{
    public static ResolvedBatchSetEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, []);
}

public sealed record ResolvedBatchSeriesEdit(
    BatchMetadataFieldOperation Operation,
    Guid? Value)
{
    public static ResolvedBatchSeriesEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, null);
}

public sealed record ResolvedBatchMetadataEdits(
    ResolvedBatchSetEdit Authors,
    ResolvedBatchSeriesEdit Series,
    ResolvedBatchSetEdit Tags);

public sealed record BatchTaxonomyToken(
    TaxonomyKind Kind,
    Guid? ExistingId,
    TaxonomyName? NewName);

public sealed record BatchMetadataWrite(
    IReadOnlyList<InboxSelectionEntry> Selection,
    BatchMetadataMode Mode,
    BatchMetadataFieldOperation AuthorOperation,
    IReadOnlyList<BatchTaxonomyToken> Authors,
    BatchMetadataFieldOperation SeriesOperation,
    BatchTaxonomyToken? Series,
    BatchMetadataFieldOperation TagOperation,
    IReadOnlyList<BatchTaxonomyToken> Tags);

public sealed record BatchMetadataResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int AffectedCount,
    string? ErrorCode);

public interface IBatchMetadataStore
{
    Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataWrite write,
        CancellationToken cancellationToken);
}

public interface IBatchMetadataService
{
    Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataCommand command,
        CancellationToken cancellationToken);
}
