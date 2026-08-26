using TraceFold.Media.Application.Metadata;

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
    NamingDefinition? Naming)
{
    public static LibraryBatchEditDraft Create(
        BatchMetadataDraft? metadata,
        NamingDefinition? naming)
    {
        if (metadata is null && naming is null)
        {
            throw new ArgumentException("At least one batch edit is required.");
        }

        if (metadata is not null)
        {
            BatchMetadataDraft.Validate(metadata);
        }

        return new LibraryBatchEditDraft(metadata, naming);
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
