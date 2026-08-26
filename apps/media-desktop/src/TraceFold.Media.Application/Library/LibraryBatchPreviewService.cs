using System.Security.Cryptography;
using System.Text;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Library;

public sealed class LibraryBatchPreviewService(
    INamingRuleEngine namingRuleEngine) : ILibraryBatchPreviewService
{
    public LibraryBatchPreviewDto Preview(
        LibraryBatchEditorDto editor,
        LibraryBatchEditDraft draft)
    {
        Validate(editor, draft);
        var catalog = TermCatalog.Create(editor);
        ResolvedBatchMetadataEdits edits = draft.Metadata is null
            ? UnchangedEdits()
            : Resolve(draft.Metadata, catalog);
        ProposedAsset[] proposed = editor.Assets
            .OrderBy(asset => asset.Selection.Ordinal)
            .Select(asset => ApplyMetadata(asset, edits, catalog))
            .ToArray();

        NamingPreviewResult? namingPreview = draft.Naming is null
            ? null
            : namingRuleEngine.Preview(
                draft.Naming,
                proposed.Select(ToNamingInput).ToArray(),
                editor.UnselectedDisplayNames);
        IReadOnlyDictionary<Guid, NamingPreviewRow> namingRows =
            namingPreview?.Rows.ToDictionary(row => row.AssetId)
            ?? new Dictionary<Guid, NamingPreviewRow>();
        LibraryBatchPreviewRowDto[] rows = proposed
            .Select(asset => Merge(asset, namingRows))
            .ToArray();
        int changedCount = rows.Count(row =>
            row.MetadataChanged || row.DisplayNameChanged);
        int warningCount = namingPreview?.WarningCount ?? 0;
        int errorCount = namingPreview?.ErrorCount ?? 0;
        string? errorCode = errorCount > 0
            ? "library.batch.preview_invalid"
            : changedCount == 0
                ? "library.batch.no_changes"
                : null;
        return new LibraryBatchPreviewDto(
            Array.AsReadOnly(rows),
            errorCode is null,
            changedCount,
            warningCount,
            errorCount,
            errorCode);
    }

    private static ProposedAsset ApplyMetadata(
        LibraryBatchAssetDto asset,
        ResolvedBatchMetadataEdits edits,
        TermCatalog catalog)
    {
        MetadataSelection current = MetadataSelection.Create(
            asset.ExplicitDisplayName,
            asset.Authors.Select(term => term.Id),
            asset.Series?.Id,
            asset.Tags.Select(term => term.Id));
        MetadataSelection proposed = BatchMetadataMutation.Apply(current, edits);
        bool changed = !proposed.AuthorIds.SequenceEqual(current.AuthorIds)
            || proposed.SeriesId != current.SeriesId
            || !proposed.TagIds.SequenceEqual(current.TagIds);
        return new ProposedAsset(
            asset,
            proposed.AuthorIds.Select(catalog.Get).ToArray(),
            proposed.SeriesId.HasValue
                ? catalog.Get(proposed.SeriesId.Value)
                : null,
            proposed.TagIds.Select(catalog.Get).ToArray(),
            changed);
    }

    private static NamingAssetInput ToNamingInput(ProposedAsset asset) =>
        new(
            asset.Asset.Selection.AssetId,
            asset.Asset.ActualFilename,
            asset.Asset.ExplicitDisplayName,
            asset.Authors.Select(term => term.DisplayName).ToArray(),
            asset.Series?.DisplayName,
            asset.Tags.Select(term => term.DisplayName).ToArray(),
            asset.Asset.Selection.Ordinal);

    private static LibraryBatchPreviewRowDto Merge(
        ProposedAsset asset,
        IReadOnlyDictionary<Guid, NamingPreviewRow> namingRows)
    {
        namingRows.TryGetValue(
            asset.Asset.Selection.AssetId,
            out NamingPreviewRow? naming);
        string currentDisplayName = asset.Asset.ExplicitDisplayName
            ?? Path.GetFileNameWithoutExtension(asset.Asset.ActualFilename);
        return new LibraryBatchPreviewRowDto(
            asset.Asset.Selection.AssetId,
            asset.Asset.Selection.Ordinal,
            asset.Asset.ActualFilename,
            currentDisplayName,
            naming?.GeneratedName,
            asset.MetadataChanged,
            naming?.IsChanged ?? false,
            naming?.Issues ?? Array.Empty<NamingIssue>());
    }

    private static ResolvedBatchMetadataEdits Resolve(
        BatchMetadataDraft draft,
        TermCatalog catalog) =>
        new(
            ResolveSet(draft.Authors, TaxonomyKind.Author, catalog),
            ResolveSeries(draft.Series, catalog),
            ResolveSet(draft.Tags, TaxonomyKind.Tag, catalog));

    private static ResolvedBatchSetEdit ResolveSet(
        BatchMetadataSetEdit edit,
        TaxonomyKind kind,
        TermCatalog catalog) =>
        new(
            edit.Operation,
            edit.Values.Select(value => catalog.Resolve(value, kind)).ToArray());

    private static ResolvedBatchSeriesEdit ResolveSeries(
        BatchMetadataSeriesEdit edit,
        TermCatalog catalog) =>
        new(
            edit.Operation,
            edit.Value is null
                ? null
                : catalog.Resolve(edit.Value, TaxonomyKind.Series));

    private static ResolvedBatchMetadataEdits UnchangedEdits() =>
        new(
            ResolvedBatchSetEdit.Unchanged,
            ResolvedBatchSeriesEdit.Unchanged,
            ResolvedBatchSetEdit.Unchanged);

    private static void Validate(
        LibraryBatchEditorDto editor,
        LibraryBatchEditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(editor.Assets);
        ArgumentNullException.ThrowIfNull(editor.Authors);
        ArgumentNullException.ThrowIfNull(editor.Tags);
        ArgumentNullException.ThrowIfNull(editor.UnselectedDisplayNames);
        if (draft.Metadata is null && draft.Naming is null)
        {
            throw new ArgumentException("At least one batch edit is required.");
        }

        if (draft.Metadata is not null)
        {
            BatchMetadataDraft.Validate(draft.Metadata);
        }

        if (editor.Assets.Count == 0
            || editor.UnselectedDisplayNames.Any(name => name is null))
        {
            throw new ArgumentException("The library batch editor is invalid.");
        }

        var ids = new HashSet<Guid>();
        var ordinals = new HashSet<int>();
        foreach (LibraryBatchAssetDto? asset in editor.Assets)
        {
            if (asset is null
                || asset.Selection is null
                || asset.Selection.AssetId == Guid.Empty
                || asset.Selection.ExpectedMetadataVersion < 0
                || asset.Selection.Ordinal < 0
                || string.IsNullOrWhiteSpace(asset.ActualFilename)
                || asset.Authors is null
                || asset.Tags is null
                || !ids.Add(asset.Selection.AssetId)
                || !ordinals.Add(asset.Selection.Ordinal))
            {
                throw new ArgumentException("The library selection is invalid.");
            }

            ValidateTerms(asset.Authors, TaxonomyKind.Author);
            ValidateTerms(asset.Tags, TaxonomyKind.Tag);
            if (asset.Series is not null)
            {
                ValidateTerm(asset.Series, TaxonomyKind.Series);
            }
        }

        ValidateCoverage(editor.Authors, TaxonomyKind.Author, editor.Assets);
        ValidateCoverage(editor.Tags, TaxonomyKind.Tag, editor.Assets);
    }

    private static void ValidateCoverage(
        IReadOnlyList<BatchTermCoverageDto> coverage,
        TaxonomyKind kind,
        IReadOnlyList<LibraryBatchAssetDto> assets)
    {
        var ids = new HashSet<Guid>();
        foreach (BatchTermCoverageDto? item in coverage)
        {
            if (item is null
                || !ids.Add(item.Term.Id)
                || item.SelectedCount < 0
                || item.SelectedCount > assets.Count)
            {
                throw new ArgumentException("The taxonomy coverage is invalid.");
            }

            ValidateTerm(item.Term, kind);
            int expected = kind == TaxonomyKind.Author
                ? assets.Count(asset => asset.Authors.Any(
                    term => term.Id == item.Term.Id))
                : assets.Count(asset => asset.Tags.Any(
                    term => term.Id == item.Term.Id));
            if (item.SelectedCount != expected)
            {
                throw new ArgumentException("The taxonomy coverage is stale.");
            }
        }
    }

    private static void ValidateTerms(
        IReadOnlyList<TaxonomyTermDto> terms,
        TaxonomyKind kind)
    {
        var ids = new HashSet<Guid>();
        foreach (TaxonomyTermDto? term in terms)
        {
            if (term is null || !ids.Add(term.Id))
            {
                throw new ArgumentException("The taxonomy terms are invalid.");
            }

            ValidateTerm(term, kind);
        }
    }

    private static void ValidateTerm(
        TaxonomyTermDto term,
        TaxonomyKind kind)
    {
        TaxonomyName name;
        try
        {
            name = TaxonomyName.Create(term.DisplayName);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException("The taxonomy term is invalid.");
        }

        if (term.Id == Guid.Empty
            || term.Kind != kind
            || !StringComparer.Ordinal.Equals(
                term.NormalizedName,
                name.Normalized))
        {
            throw new ArgumentException("The taxonomy term is invalid.");
        }
    }

    private sealed record ProposedAsset(
        LibraryBatchAssetDto Asset,
        IReadOnlyList<TaxonomyTermDto> Authors,
        TaxonomyTermDto? Series,
        IReadOnlyList<TaxonomyTermDto> Tags,
        bool MetadataChanged);

    private sealed class TermCatalog
    {
        private readonly Dictionary<Guid, TaxonomyTermDto> _byId = [];
        private readonly Dictionary<TermNameKey, TaxonomyTermDto> _byName = [];

        public static TermCatalog Create(LibraryBatchEditorDto editor)
        {
            var catalog = new TermCatalog();
            foreach (LibraryBatchAssetDto asset in editor.Assets)
            {
                foreach (TaxonomyTermDto author in asset.Authors)
                {
                    catalog.AddExisting(author);
                }

                if (asset.Series is not null)
                {
                    catalog.AddExisting(asset.Series);
                }

                foreach (TaxonomyTermDto tag in asset.Tags)
                {
                    catalog.AddExisting(tag);
                }
            }

            foreach (BatchTermCoverageDto author in editor.Authors)
            {
                catalog.AddExisting(author.Term);
            }

            foreach (BatchTermCoverageDto tag in editor.Tags)
            {
                catalog.AddExisting(tag.Term);
            }

            return catalog;
        }

        public TaxonomyTermDto Get(Guid id) =>
            _byId.TryGetValue(id, out TaxonomyTermDto? term)
                ? term
                : throw new ArgumentException("The taxonomy term is missing.");

        public Guid Resolve(
            BatchTaxonomyReference reference,
            TaxonomyKind expectedKind)
        {
            ArgumentNullException.ThrowIfNull(reference);
            if (reference.Kind != expectedKind)
            {
                throw new ArgumentException("The taxonomy kind is invalid.");
            }

            if (reference.ExistingId.HasValue)
            {
                if (reference.ExistingId.Value == Guid.Empty
                    || reference.NewName is not null)
                {
                    throw new ArgumentException(
                        "The taxonomy reference is invalid.");
                }

                TaxonomyTermDto existing = Get(reference.ExistingId.Value);
                if (existing.Kind != expectedKind)
                {
                    throw new ArgumentException("The taxonomy kind is invalid.");
                }

                return existing.Id;
            }

            TaxonomyName name = TaxonomyName.Create(reference.NewName!);
            var key = new TermNameKey(expectedKind, name.Normalized);
            if (_byName.TryGetValue(key, out TaxonomyTermDto? matched))
            {
                return matched.Id;
            }

            Guid id = CreateDeferredId(key);
            var deferred = new TaxonomyTermDto(
                id,
                expectedKind,
                name.Display,
                name.Normalized,
                0);
            _byId.Add(id, deferred);
            _byName.Add(key, deferred);
            return id;
        }

        private void AddExisting(TaxonomyTermDto term)
        {
            var key = new TermNameKey(term.Kind, term.NormalizedName);
            if (_byId.TryGetValue(term.Id, out TaxonomyTermDto? byId)
                && byId != term)
            {
                throw new ArgumentException("A taxonomy id is inconsistent.");
            }

            if (_byName.TryGetValue(key, out TaxonomyTermDto? byName)
                && byName.Id != term.Id)
            {
                throw new ArgumentException("A taxonomy name is ambiguous.");
            }

            _byId.TryAdd(term.Id, term);
            _byName.TryAdd(key, term);
        }

        private Guid CreateDeferredId(TermNameKey key)
        {
            for (int salt = 0; ; salt++)
            {
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"tracefold-preview:{key.Kind}:{key.NormalizedName}:{salt}"));
                var id = new Guid(hash.AsSpan(0, 16));
                if (id != Guid.Empty && !_byId.ContainsKey(id))
                {
                    return id;
                }
            }
        }

        private readonly record struct TermNameKey(
            TaxonomyKind Kind,
            string NormalizedName);
    }
}
