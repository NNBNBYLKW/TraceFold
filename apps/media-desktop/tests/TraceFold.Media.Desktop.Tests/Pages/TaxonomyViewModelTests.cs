using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class TaxonomyViewModelTests
{
    [Fact]
    public void Kind_options_use_the_approved_labels()
    {
        Fixture fixture = new();

        Assert.Collection(
            fixture.ViewModel.KindOptions,
            item => Assert.Equal((TaxonomyKind.Author, "作者"),
                (item.Kind, item.DisplayName)),
            item => Assert.Equal((TaxonomyKind.Series, "系列"),
                (item.Kind, item.DisplayName)),
            item => Assert.Equal((TaxonomyKind.Tag, "内容标签"),
                (item.Kind, item.DisplayName)));
    }

    [Fact]
    public async Task Search_and_reference_filter_changes_clear_selection_and_report_it()
    {
        Fixture fixture = new(Term("Alpha"), Term("Beta"));
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([fixture.ViewModel.Terms[0]]);

        fixture.ViewModel.SearchText = "Alpha";

        Assert.Empty(fixture.ViewModel.Selection.SelectedIds);
        Assert.Equal("筛选条件已更改，已清除选择。", fixture.ViewModel.StatusMessage);

        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([fixture.ViewModel.Terms[0]]);
        fixture.ViewModel.ReferenceFilter = TaxonomyReferenceFilter.Unused;

        Assert.Empty(fixture.ViewModel.Selection.SelectedIds);
        Assert.Equal("筛选条件已更改，已清除选择。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Sort_changes_reconcile_selection_in_the_new_query_order()
    {
        Fixture fixture = new(Term("A"), Term("B"));
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection(fixture.ViewModel.Terms.ToArray());

        fixture.ViewModel.SortDirection = TaxonomySortDirection.Descending;
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            fixture.ViewModel.Terms.Select(term => term.Id),
            fixture.ViewModel.Selection.SelectedIds);
    }

    [Fact]
    public async Task Mixed_selection_enables_merge_but_lists_all_delete_blockers()
    {
        TaxonomyManagementTermDto first = Term("A", inboxReferences: 1);
        TaxonomyManagementTermDto unused = Term("B");
        TaxonomyManagementTermDto second = Term("C", libraryReferences: 2);
        Fixture fixture = new(first, unused, second);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);

        fixture.ViewModel.ReplaceSelection(fixture.ViewModel.Terms.ToArray());

        Assert.True(fixture.ViewModel.CanMerge);
        Assert.False(fixture.ViewModel.CanDeleteSelection);
        Assert.Equal([first.Id, second.Id],
            fixture.ViewModel.DeleteBlockers.Select(term => term.Id));
    }

    [Fact]
    public async Task Delete_is_enabled_only_when_every_selected_term_is_unused()
    {
        TaxonomyManagementTermDto unused = Term("Unused");
        TaxonomyManagementTermDto used = Term("Used", inboxReferences: 1);
        Fixture fixture = new(unused, used);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);

        fixture.ViewModel.ReplaceSelection([unused]);
        Assert.True(fixture.ViewModel.CanDeleteSelection);

        fixture.ViewModel.ReplaceSelection([unused, used]);
        Assert.False(fixture.ViewModel.CanDeleteSelection);

        fixture.ViewModel.ReplaceSelection([]);
        Assert.False(fixture.ViewModel.CanDeleteSelection);
    }

    [Fact]
    public async Task Successful_merge_reloads_and_keeps_the_remaining_selected_target()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        Fixture fixture = new(source, target);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([source, target]);
        fixture.Dialogs.MergeHandler = (selected, _) =>
        {
            Assert.Equal([source.Id, target.Id], selected.Select(term => term.Id));
            fixture.Terms.RemoveAll(term => term.Id == source.Id);
            return Success(canUndo: true);
        };

        await fixture.ViewModel.MergeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([target.Id], fixture.ViewModel.Selection.SelectedIds);
        Assert.Equal("词条已合并。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Successful_delete_reloads_and_removes_deleted_selection()
    {
        TaxonomyManagementTermDto unused = Term("Unused");
        Fixture fixture = new(unused);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([unused]);
        fixture.Maintenance.DeleteResult = Success(canUndo: true);
        fixture.Maintenance.DeleteHandler = preparation =>
        {
            fixture.Terms.RemoveAll(term =>
                preparation.Selection.TermIds.Contains(term.Id));
        };

        await fixture.ViewModel.DeleteSelectionAsync(
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.ViewModel.Terms);
        Assert.Empty(fixture.ViewModel.Selection.SelectedIds);
        Assert.Equal("词条已删除。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Cancelled_or_failed_merge_dialog_retains_selection()
    {
        TaxonomyManagementTermDto term = Term("Source");
        Fixture fixture = new(term);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([term]);
        fixture.Dialogs.MergeResult = new TaxonomyMaintenanceResult(
            false, 0, 0, 0, false,
            TaxonomyMaintenanceErrorCodes.PreviewStale);

        await fixture.ViewModel.MergeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([term.Id], fixture.ViewModel.Selection.SelectedIds);
        Assert.Equal("合并预览已过期，请重新确认。", fixture.ViewModel.StatusMessage);

        fixture.Dialogs.MergeResult = null;
        await fixture.ViewModel.MergeAsync(TestContext.Current.CancellationToken);
        Assert.Equal([term.Id], fixture.ViewModel.Selection.SelectedIds);
    }

    [Fact]
    public void Undo_event_controls_banner_and_command_state()
    {
        Fixture fixture = new();
        Assert.False(fixture.ViewModel.CanUndoMaintenance);
        Assert.Equal(string.Empty, fixture.ViewModel.UndoBannerMessage);

        fixture.Maintenance.SetUndoState(new TaxonomyMaintenanceUndoState(
            true, 3, 2));

        Assert.True(fixture.ViewModel.CanUndoMaintenance);
        Assert.True(fixture.ViewModel.UndoCommand.CanExecute(null));
        Assert.Equal(
            "可撤销上一次词表维护：影响 3 项资料、2 个词条。",
            fixture.ViewModel.UndoBannerMessage);
    }

    [Fact]
    public async Task Rename_conflict_resolves_exact_normalized_target_without_merging()
    {
        TaxonomyManagementTermDto original = Term("Original");
        TaxonomyManagementTermDto near = Term("Existing 2");
        TaxonomyManagementTermDto conflict = Term("Ｅxisting", normalized: "EXISTING");
        Fixture fixture = new(original, near, conflict);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([original]);
        fixture.Taxonomy.RenameResult = new TaxonomyMutationResult(
            false, "taxonomy.name_conflict");

        await fixture.ViewModel.RenameAsync(
            "Existing",
            TestContext.Current.CancellationToken);

        Assert.Equal(conflict.Id, fixture.ViewModel.RenameConflictTarget?.Id);
        Assert.True(fixture.ViewModel.CanMergeRenameConflict);
        Assert.Equal(0, fixture.Dialogs.MergeCallCount);
        Assert.Equal("词条名称已存在，可改为合并。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Rename_conflict_merge_passes_the_exact_target_to_the_dialog()
    {
        TaxonomyManagementTermDto original = Term("Original");
        TaxonomyManagementTermDto conflict = Term("Existing");
        Fixture fixture = new(original, conflict);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([original]);
        fixture.Taxonomy.RenameResult = new TaxonomyMutationResult(
            false, "taxonomy.name_conflict");
        await fixture.ViewModel.RenameAsync(
            "Existing",
            TestContext.Current.CancellationToken);
        fixture.Dialogs.MergeResult = null;

        await fixture.ViewModel.MergeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(conflict.Id, fixture.Dialogs.LastPreselectedTargetId);
        Assert.Equal([original.Id],
            fixture.Dialogs.LastSelectedTerms.Select(term => term.Id));
    }

    [Fact]
    public async Task Successful_create_and_ordinary_rename_invalidate_maintenance_undo()
    {
        TaxonomyManagementTermDto original = Term("Original");
        Fixture fixture = new(original);
        await fixture.ViewModel.LoadAsync(TestContext.Current.CancellationToken);

        await fixture.ViewModel.CreateAsync(
            "Created",
            TestContext.Current.CancellationToken);
        fixture.ViewModel.ReplaceSelection([
            Assert.Single(fixture.ViewModel.Terms, term => term.DisplayName == "Original")
        ]);
        await fixture.ViewModel.RenameAsync(
            "Renamed",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, fixture.Maintenance.InvalidateUndoCallCount);
        Assert.Null(fixture.ViewModel.RenameConflictTarget);
    }

    private static TaxonomyMaintenanceResult Success(bool canUndo) =>
        new(true, 0, 0, 1, canUndo, null);

    private static TaxonomyManagementTermDto Term(
        string displayName,
        int inboxReferences = 0,
        int libraryReferences = 0,
        string? normalized = null) =>
        new(
            Guid.NewGuid(),
            TaxonomyKind.Author,
            displayName,
            normalized ?? displayName.ToUpperInvariant(),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            inboxReferences,
            libraryReferences);

    private sealed class Fixture
    {
        public Fixture(params TaxonomyManagementTermDto[] terms)
        {
            Terms = [.. terms];
            Query = new FakeQueryService(Terms);
            Taxonomy = new FakeTaxonomyService(Terms);
            Maintenance = new FakeMaintenanceService(Terms);
            Dialogs = new FakeDialogService();
            ViewModel = new TaxonomyViewModel(
                Query,
                Taxonomy,
                Maintenance,
                Dialogs);
        }

        public List<TaxonomyManagementTermDto> Terms { get; }

        public FakeQueryService Query { get; }

        public FakeTaxonomyService Taxonomy { get; }

        public FakeMaintenanceService Maintenance { get; }

        public FakeDialogService Dialogs { get; }

        public TaxonomyViewModel ViewModel { get; }
    }

    private sealed class FakeQueryService(List<TaxonomyManagementTermDto> terms)
        : ITaxonomyManagementQueryService
    {
        public List<TaxonomyManagementQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
            TaxonomyManagementQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            IEnumerable<TaxonomyManagementTermDto> result = terms
                .Where(term => term.Kind == query.Kind)
                .Where(term => query.SearchText is null
                    || term.DisplayName.Contains(
                        query.SearchText,
                        StringComparison.OrdinalIgnoreCase))
                .Where(term => query.ReferenceFilter switch
                {
                    TaxonomyReferenceFilter.Referenced => term.ReferenceCount > 0,
                    TaxonomyReferenceFilter.Unused => term.ReferenceCount == 0,
                    _ => true
                });
            result = (query.SortField, query.SortDirection) switch
            {
                (TaxonomySortField.ReferenceCount,
                    TaxonomySortDirection.Ascending) =>
                    result.OrderBy(term => term.ReferenceCount)
                        .ThenBy(term => term.DisplayName),
                (TaxonomySortField.ReferenceCount,
                    TaxonomySortDirection.Descending) =>
                    result.OrderByDescending(term => term.ReferenceCount)
                        .ThenBy(term => term.DisplayName),
                (_, TaxonomySortDirection.Descending) =>
                    result.OrderByDescending(term => term.DisplayName),
                _ => result.OrderBy(term => term.DisplayName)
            };
            return Task.FromResult<IReadOnlyList<TaxonomyManagementTermDto>>(
                result.ToArray());
        }
    }

    private sealed class FakeTaxonomyService(List<TaxonomyManagementTermDto> terms)
        : ITaxonomyService
    {
        public TaxonomyMutationResult RenameResult { get; set; } =
            new(true, null);

        public Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
            TaxonomyKind kind,
            string? query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaxonomyTermDto>>([]);

        public Task<TaxonomyTermDto> CreateAsync(
            TaxonomyKind kind,
            string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = new TaxonomyManagementTermDto(
                Guid.NewGuid(),
                kind,
                name.Trim(),
                TaxonomyName.Create(name).Normalized,
                DateTimeOffset.Parse("2026-08-29T01:00:00Z"),
                0,
                0);
            terms.Add(created);
            return Task.FromResult(new TaxonomyTermDto(
                created.Id,
                created.Kind,
                created.DisplayName,
                created.NormalizedName,
                0));
        }

        public Task<TaxonomyMutationResult> RenameAsync(
            Guid id,
            string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RenameResult.Succeeded)
            {
                int index = terms.FindIndex(term => term.Id == id);
                if (index >= 0)
                {
                    terms[index] = terms[index] with
                    {
                        DisplayName = name.Trim(),
                        NormalizedName = TaxonomyName.Create(name).Normalized
                    };
                }
            }

            return Task.FromResult(RenameResult);
        }

        public Task<TaxonomyMutationResult> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMutationResult(true, null));
    }

    private sealed class FakeDialogService : ITaxonomyMaintenanceDialogService
    {
        public TaxonomyMaintenanceResult? MergeResult { get; set; }

        public Func<IReadOnlyList<TaxonomyManagementTermDto>, Guid?,
            TaxonomyMaintenanceResult?>? MergeHandler { get; set; }

        public bool ConfirmDelete { get; set; } = true;

        public int MergeCallCount { get; private set; }

        public Guid? LastPreselectedTargetId { get; private set; }

        public IReadOnlyList<TaxonomyManagementTermDto> LastSelectedTerms
            { get; private set; } = [];

        public Task<TaxonomyMaintenanceResult?> ShowMergeAsync(
            IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
            Guid? preselectedTargetId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MergeCallCount++;
            LastSelectedTerms = selectedTerms;
            LastPreselectedTargetId = preselectedTargetId;
            return Task.FromResult(MergeHandler?.Invoke(
                selectedTerms,
                preselectedTargetId) ?? MergeResult);
        }

        public Task<bool> ConfirmDeleteAsync(
            TaxonomyDeletePreparation preparation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConfirmDelete);
        }
    }

    private sealed class FakeMaintenanceService(
        List<TaxonomyManagementTermDto> terms)
        : ITaxonomyMaintenanceService
    {
        public TaxonomyMaintenanceUndoState UndoState { get; private set; } =
            TaxonomyMaintenanceUndoState.Unavailable;

        public event EventHandler? UndoStateChanged;

        public int InvalidateUndoCallCount { get; private set; }

        public TaxonomyMaintenanceResult DeleteResult { get; set; } =
            Success(canUndo: true);

        public Action<TaxonomyDeletePreparation>? DeleteHandler { get; set; }

        public void SetUndoState(TaxonomyMaintenanceUndoState state)
        {
            UndoState = state;
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
            TaxonomyMergeSelection selection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public TaxonomyMergePreviewDto PreviewMerge(
            TaxonomyMergePreparation preparation,
            string? finalNameOverride) =>
            throw new NotSupportedException();

        public Task<TaxonomyMaintenanceResult> ApplyMergeAsync(
            TaxonomyMergePreparation preparation,
            string? finalNameOverride,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
            TaxonomyDeleteSelection selection,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaxonomyTermSnapshot[] snapshots = terms
                .Where(term => selection.TermIds.Contains(term.Id))
                .Select(term => new TaxonomyTermSnapshot(
                    term.Id,
                    term.Kind,
                    term.DisplayName,
                    term.NormalizedName,
                    term.UpdatedAtUtc.AddHours(-1),
                    term.UpdatedAtUtc))
                .ToArray();
            TaxonomyDeleteBlocker[] blockers = terms
                .Where(term => selection.TermIds.Contains(term.Id))
                .Where(term => term.ReferenceCount > 0)
                .Select(term => new TaxonomyDeleteBlocker(
                    snapshots.Single(snapshot => snapshot.Id == term.Id),
                    term.InboxReferenceCount,
                    term.LibraryReferenceCount))
                .ToArray();
            var preparation = new TaxonomyDeletePreparation(
                Guid.NewGuid(),
                selection,
                snapshots,
                blockers);
            return Task.FromResult(new TaxonomyDeletePrepareResult(
                true,
                preparation,
                null));
        }

        public Task<TaxonomyMaintenanceResult> DeleteUnusedAsync(
            TaxonomyDeletePreparation preparation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteHandler?.Invoke(preparation);
            return Task.FromResult(DeleteResult);
        }

        public Task<TaxonomyMaintenanceUndoResult> UndoLastAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMaintenanceUndoResult(
                true, 0, 0, [], false, null));

        public void InvalidateUndo()
        {
            InvalidateUndoCallCount++;
            SetUndoState(TaxonomyMaintenanceUndoState.Unavailable);
        }
    }
}
