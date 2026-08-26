using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Pages;

public sealed record LibraryFilterChipViewModel(
    string Key,
    string Label,
    Action Remove);

public sealed partial class LibraryFilterViewModel : ObservableObject
{
    public LibraryFilterViewModel(ITaxonomySearchService taxonomy)
    {
        Authors = new ExistingTaxonomyFilterViewModel(taxonomy, TaxonomyKind.Author);
        Series = new ExistingTaxonomyFilterViewModel(taxonomy, TaxonomyKind.Series);
        Tags = new ExistingTaxonomyFilterViewModel(taxonomy, TaxonomyKind.Tag);
        Authors.SelectionChanged += OnAuthorsChanged;
        Series.SelectionChanged += OnSeriesChanged;
        Tags.SelectionChanged += OnTagsChanged;
        SynchronizeDraftFromApplied();
    }

    public ExistingTaxonomyFilterViewModel Authors { get; }

    public ExistingTaxonomyFilterViewModel Series { get; }

    public ExistingTaxonomyFilterViewModel Tags { get; }

    public LibraryQuery AppliedQuery { get; private set; } = LibraryQuery.Default;

    public ObservableCollection<LibraryFilterChipViewModel> ActiveChips { get; } = [];

    public int ActiveFilterCount => ActiveChips.Count;

    [ObservableProperty]
    public partial LibraryMediaFilter Media { get; set; }

    [ObservableProperty]
    public partial bool MissingAuthors { get; set; }

    [ObservableProperty]
    public partial bool MissingSeries { get; set; }

    [ObservableProperty]
    public partial bool MissingTags { get; set; }

    [ObservableProperty]
    public partial LibraryDisplayNameFilter DisplayName { get; set; }

    public event EventHandler? AppliedQueryChanged;

    public void BeginEdit() => SynchronizeDraftFromApplied();

    public void CancelDraft() => SynchronizeDraftFromApplied();

    public LibraryQuery ApplyDraft()
    {
        LibraryQuery query = LibraryQuery.Create(
            AppliedQuery.SearchText,
            Media,
            Authors.SelectedTerms.Select(term => term.Id).ToArray(),
            Series.SelectedTerms.FirstOrDefault()?.Id,
            Tags.SelectedTerms.Select(term => term.Id).ToArray(),
            MissingAuthors,
            MissingSeries,
            MissingTags,
            DisplayName,
            AppliedQuery.SortField,
            AppliedQuery.SortDirection);
        SetAppliedQuery(query, synchronizeDraft: false);
        return query;
    }

    public void SetAppliedQuery(LibraryQuery query) =>
        SetAppliedQuery(query, synchronizeDraft: true);

    public LibraryQuery UpdateSearch(string? searchText)
    {
        LibraryQuery current = AppliedQuery;
        LibraryQuery query = LibraryQuery.Create(
            searchText,
            current.Media,
            current.AuthorIds,
            current.SeriesId,
            current.TagIds,
            current.MissingAuthors,
            current.MissingSeries,
            current.MissingTags,
            current.DisplayName,
            current.SortField,
            current.SortDirection);
        SetAppliedQuery(query);
        return query;
    }

    public LibraryQuery UpdateSort(
        LibrarySortField field,
        LibrarySortDirection direction)
    {
        LibraryQuery current = AppliedQuery;
        LibraryQuery query = LibraryQuery.Create(
            current.SearchText,
            current.Media,
            current.AuthorIds,
            current.SeriesId,
            current.TagIds,
            current.MissingAuthors,
            current.MissingSeries,
            current.MissingTags,
            current.DisplayName,
            field,
            direction);
        SetAppliedQuery(query);
        return query;
    }

    public void RemoveChip(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key == "media")
        {
            Media = LibraryMediaFilter.All;
        }
        else if (key == "missing-authors")
        {
            MissingAuthors = false;
        }
        else if (key == "missing-series")
        {
            MissingSeries = false;
        }
        else if (key == "missing-tags")
        {
            MissingTags = false;
        }
        else if (key == "display-name")
        {
            DisplayName = LibraryDisplayNameFilter.All;
        }
        else if (TryReadId(key, "author:", out Guid authorId))
        {
            RemoveById(Authors, authorId);
        }
        else if (TryReadId(key, "series:", out Guid seriesId))
        {
            RemoveById(Series, seriesId);
        }
        else if (TryReadId(key, "tag:", out Guid tagId))
        {
            RemoveById(Tags, tagId);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }

        ApplyDraft();
    }

    public void ClearAll()
    {
        Media = LibraryMediaFilter.All;
        MissingAuthors = false;
        MissingSeries = false;
        MissingTags = false;
        DisplayName = LibraryDisplayNameFilter.All;
        Authors.Clear();
        Series.Clear();
        Tags.Clear();
        ApplyDraft();
    }

    partial void OnMissingAuthorsChanged(bool value)
    {
        if (value)
        {
            Authors.Clear();
        }
    }

    partial void OnMissingSeriesChanged(bool value)
    {
        if (value)
        {
            Series.Clear();
        }
    }

    partial void OnMissingTagsChanged(bool value)
    {
        if (value)
        {
            Tags.Clear();
        }
    }

    private void SetAppliedQuery(LibraryQuery query, bool synchronizeDraft)
    {
        ArgumentNullException.ThrowIfNull(query);
        AppliedQuery = query;
        if (synchronizeDraft)
        {
            SynchronizeDraftFromApplied();
        }

        RebuildChips();
        OnPropertyChanged(nameof(AppliedQuery));
        AppliedQueryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeDraftFromApplied()
    {
        LibraryQuery query = AppliedQuery;
        Media = query.Media;
        MissingAuthors = false;
        MissingSeries = false;
        MissingTags = false;
        DisplayName = query.DisplayName;
        Authors.SetSelectedTerms(ResolveTerms(Authors, query.AuthorIds));
        Series.SetSelectedTerms(ResolveTerms(
            Series,
            query.SeriesId.HasValue ? [query.SeriesId.Value] : []));
        Tags.SetSelectedTerms(ResolveTerms(Tags, query.TagIds));
        MissingAuthors = query.MissingAuthors;
        MissingSeries = query.MissingSeries;
        MissingTags = query.MissingTags;
    }

    private void RebuildChips()
    {
        ActiveChips.Clear();
        if (AppliedQuery.Media != LibraryMediaFilter.All)
        {
            AddChip(
                "media",
                AppliedQuery.Media == LibraryMediaFilter.Image
                    ? "媒体：图片"
                    : "媒体：视频");
        }

        AddTermChips("author:", "作者：", Authors, AppliedQuery.AuthorIds);
        AddTermChips(
            "series:",
            "系列：",
            Series,
            AppliedQuery.SeriesId.HasValue ? [AppliedQuery.SeriesId.Value] : []);
        AddTermChips("tag:", "标签：", Tags, AppliedQuery.TagIds);
        if (AppliedQuery.MissingAuthors)
        {
            AddChip("missing-authors", "缺少作者");
        }

        if (AppliedQuery.MissingSeries)
        {
            AddChip("missing-series", "缺少系列");
        }

        if (AppliedQuery.MissingTags)
        {
            AddChip("missing-tags", "缺少标签");
        }

        if (AppliedQuery.DisplayName != LibraryDisplayNameFilter.All)
        {
            AddChip(
                "display-name",
                AppliedQuery.DisplayName == LibraryDisplayNameFilter.Customized
                    ? "显示名称：已自定义"
                    : "显示名称：实际文件名");
        }

        OnPropertyChanged(nameof(ActiveFilterCount));
    }

    private void AddTermChips(
        string keyPrefix,
        string labelPrefix,
        ExistingTaxonomyFilterViewModel filter,
        IReadOnlyList<Guid> ids)
    {
        Dictionary<Guid, TaxonomyTermDto> terms = filter.SelectedTerms
            .Concat(filter.Suggestions)
            .DistinctBy(term => term.Id)
            .ToDictionary(term => term.Id);
        foreach (Guid id in ids)
        {
            string label = terms.TryGetValue(id, out TaxonomyTermDto? term)
                ? term.DisplayName
                : id.ToString("D");
            AddChip($"{keyPrefix}{id:N}", $"{labelPrefix}{label}");
        }
    }

    private void AddChip(string key, string label)
    {
        string captured = key;
        ActiveChips.Add(new LibraryFilterChipViewModel(
            key,
            label,
            () => RemoveChip(captured)));
    }

    private static IReadOnlyList<TaxonomyTermDto> ResolveTerms(
        ExistingTaxonomyFilterViewModel filter,
        IReadOnlyList<Guid> ids)
    {
        Dictionary<Guid, TaxonomyTermDto> known = filter.SelectedTerms
            .Concat(filter.Suggestions)
            .DistinctBy(term => term.Id)
            .ToDictionary(term => term.Id);
        return ids.Select(id => known.TryGetValue(id, out TaxonomyTermDto? term)
            ? term
            : new TaxonomyTermDto(
                id,
                filter.Kind,
                id.ToString("D"),
                id.ToString("N").ToUpperInvariant(),
                0))
            .ToArray();
    }

    private static void RemoveById(
        ExistingTaxonomyFilterViewModel filter,
        Guid id)
    {
        TaxonomyTermDto? term = filter.SelectedTerms.FirstOrDefault(
            candidate => candidate.Id == id);
        if (term is not null)
        {
            filter.Remove(term);
        }
    }

    private static bool TryReadId(
        string key,
        string prefix,
        out Guid id)
    {
        id = Guid.Empty;
        return key.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(key[prefix.Length..], "N", out id);
    }

    private void OnAuthorsChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (Authors.SelectedTerms.Count > 0)
        {
            MissingAuthors = false;
        }
    }

    private void OnSeriesChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (Series.SelectedTerms.Count > 0)
        {
            MissingSeries = false;
        }
    }

    private void OnTagsChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (Tags.SelectedTerms.Count > 0)
        {
            MissingTags = false;
        }
    }
}
