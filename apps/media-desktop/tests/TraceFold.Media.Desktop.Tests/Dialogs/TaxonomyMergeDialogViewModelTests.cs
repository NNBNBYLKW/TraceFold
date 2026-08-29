using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Dialogs;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class TaxonomyMergeDialogViewModelTests
{
    [Fact]
    public async Task Selected_target_is_removed_from_sources_and_previewed()
    {
        TaxonomyManagementTermDto source = Term("作者甲");
        TaxonomyManagementTermDto target = Term("作者乙");
        Fixture fixture = new([source, target], [source, target]);

        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);

        Assert.Equal(target.Id, fixture.ViewModel.Target!.Id);
        Assert.Equal([source.Id],
            fixture.ViewModel.Sources.Select(term => term.Id));
        Assert.True(fixture.ViewModel.Preview.CanApply);
        Assert.True(fixture.ViewModel.CanApply);
    }

    [Fact]
    public async Task External_target_keeps_every_selected_term_as_a_source()
    {
        TaxonomyManagementTermDto first = Term("作者甲");
        TaxonomyManagementTermDto second = Term("作者乙");
        TaxonomyManagementTermDto external = Term("作者丙");
        Fixture fixture = new([first, second], [first, second, external]);

        await fixture.ViewModel.SelectTargetAsync(
            external,
            TestContext.Current.CancellationToken);

        Assert.Equal([first.Id, second.Id],
            fixture.ViewModel.Sources.Select(term => term.Id));
        Assert.Equal(external.Id,
            fixture.Maintenance.LastPreparation!.Selection.TargetId);
    }

    [Fact]
    public async Task Initialization_resolves_an_external_preselected_target()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto external = Term("Existing target");
        Fixture fixture = new(
            [source],
            [source, external],
            external.Id);

        await fixture.ViewModel.InitializeAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(external.Id, fixture.ViewModel.Target!.Id);
        Assert.Equal([source.Id],
            fixture.ViewModel.Sources.Select(term => term.Id));
        Assert.True(fixture.ViewModel.CanApply);
    }

    [Fact]
    public async Task Target_search_is_limited_to_the_selected_kind()
    {
        TaxonomyManagementTermDto author = Term("Alice");
        TaxonomyManagementTermDto tag = Term("Alice", TaxonomyKind.Tag);
        Fixture fixture = new([author], [author, tag]);

        await fixture.ViewModel.SearchTargetsAsync(
            "Ali",
            TestContext.Current.CancellationToken);

        TaxonomyManagementQuery query = Assert.Single(fixture.Query.Queries);
        Assert.Equal(TaxonomyKind.Author, query.Kind);
        Assert.Equal("Ali", query.SearchText);
        Assert.Equal(TaxonomyReferenceFilter.All, query.ReferenceFilter);
        Assert.Equal(TaxonomySortField.DisplayName, query.SortField);
        Assert.Equal(TaxonomySortDirection.Ascending, query.SortDirection);
        Assert.Equal([author.Id],
            fixture.ViewModel.TargetResults.Select(term => term.Id));
    }

    [Fact]
    public async Task Selecting_the_only_source_as_target_is_rejected_before_prepare()
    {
        TaxonomyManagementTermDto only = Term("Only");
        Fixture fixture = new([only], [only]);

        await fixture.ViewModel.SelectTargetAsync(
            only,
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.ViewModel.Sources);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.Equal(0, fixture.Maintenance.PrepareCallCount);
        Assert.Equal("至少保留一个源词条才能合并。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Disabling_target_rename_uses_null_override_without_losing_target()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        Fixture fixture = new([source, target], [source, target]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.RenameTarget = true;
        fixture.ViewModel.FinalNameText = "Renamed";

        fixture.ViewModel.RenameTarget = false;

        Assert.Equal(target.Id, fixture.ViewModel.Target!.Id);
        Assert.Null(fixture.Maintenance.LastPreviewNameOverride);
        Assert.True(fixture.ViewModel.Preview.CanApply);
    }

    [Fact]
    public async Task Preview_after_summary_uses_the_planned_final_target_name()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Old target");
        Fixture fixture = new([source, target], [source, target]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);

        fixture.ViewModel.RenameTarget = true;
        fixture.ViewModel.FinalNameText = "Final target";

        TaxonomyMergePreviewDisplayRow row = Assert.Single(
            fixture.ViewModel.PreviewDisplayRows);
        Assert.Contains("Final target", row.AfterSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Old target", row.AfterSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Whitespace_name_override_blocks_apply()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        Fixture fixture = new([source, target], [source, target]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);

        fixture.ViewModel.RenameTarget = true;
        fixture.ViewModel.FinalNameText = "   ";

        Assert.False(fixture.ViewModel.CanApply);
        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.NameInvalid,
            fixture.ViewModel.Preview.ErrorCode);
        Assert.Equal("请输入有效的目标名称。", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Third_term_name_collision_blocks_apply()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        TaxonomyManagementTermDto third = Term("Occupied");
        Fixture fixture = new(
            [source, target],
            [source, target, third]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);

        fixture.ViewModel.RenameTarget = true;
        fixture.ViewModel.FinalNameText = "occupied";

        Assert.False(fixture.ViewModel.CanApply);
        Assert.Equal(
            TaxonomyMaintenanceErrorCodes.NameConflict,
            fixture.ViewModel.Preview.ErrorCode);
        Assert.Equal(
            "该名称属于另一个词条，请将它改选为合并目标。",
            fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Changing_target_cancels_and_ignores_stale_preparation()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto firstTarget = Term("First");
        TaxonomyManagementTermDto secondTarget = Term("Second");
        Fixture fixture = new(
            [source, firstTarget, secondTarget],
            [source, firstTarget, secondTarget]);
        var firstCompletion = new TaskCompletionSource<TaxonomyMergePrepareResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        fixture.Maintenance.PrepareHandler = (selection, token) =>
        {
            if (selection.TargetId == firstTarget.Id)
            {
                firstToken = token;
                return firstCompletion.Task;
            }

            return Task.FromResult(fixture.Maintenance.Prepare(selection));
        };

        Task first = fixture.ViewModel.SelectTargetAsync(
            firstTarget,
            TestContext.Current.CancellationToken);
        await fixture.Maintenance.FirstPrepareStarted.Task;
        await fixture.ViewModel.SelectTargetAsync(
            secondTarget,
            TestContext.Current.CancellationToken);
        firstCompletion.SetResult(fixture.Maintenance.Prepare(
            TaxonomyMergeSelection.Create(
                TaxonomyKind.Author,
                [source.Id, secondTarget.Id],
                firstTarget.Id)));
        await first;

        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(secondTarget.Id, fixture.ViewModel.Target!.Id);
        Assert.Equal(secondTarget.Id,
            fixture.ViewModel.Preparation!.Selection.TargetId);
    }

    [Fact]
    public async Task Failed_apply_keeps_target_sources_rename_input_and_preview()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        Fixture fixture = new([source, target], [source, target]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.RenameTarget = true;
        fixture.ViewModel.FinalNameText = "Final";
        TaxonomyMergePreviewDto preview = fixture.ViewModel.Preview;
        fixture.Maintenance.ApplyResult = new TaxonomyMaintenanceResult(
            false, 0, 0, 0, false,
            TaxonomyMaintenanceErrorCodes.PreviewStale);

        await fixture.ViewModel.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(target.Id, fixture.ViewModel.Target!.Id);
        Assert.Equal([source.Id],
            fixture.ViewModel.Sources.Select(term => term.Id));
        Assert.True(fixture.ViewModel.RenameTarget);
        Assert.Equal("Final", fixture.ViewModel.FinalNameText);
        Assert.Same(preview, fixture.ViewModel.Preview);
        Assert.Equal("合并预览已过期，请重新选择目标。",
            fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Successful_apply_returns_exact_result_and_disables_repeat_submit()
    {
        TaxonomyManagementTermDto source = Term("Source");
        TaxonomyManagementTermDto target = Term("Target");
        Fixture fixture = new([source, target], [source, target]);
        await fixture.ViewModel.SelectTargetAsync(
            target,
            TestContext.Current.CancellationToken);
        fixture.Maintenance.ApplyResult = new TaxonomyMaintenanceResult(
            true, 2, 3, 1, true, null);

        TaxonomyMaintenanceResult? result = await fixture.ViewModel.ApplyAsync(
            TestContext.Current.CancellationToken);

        Assert.Same(fixture.Maintenance.ApplyResult, result);
        Assert.Same(result, fixture.ViewModel.LastResult);
        Assert.False(fixture.ViewModel.CanApply);
        Assert.False(fixture.ViewModel.ApplyCommand.CanExecute(null));
        Assert.Equal("已合并词条，更新 2 项待入库资料和 3 项正式资料。",
            fixture.ViewModel.StatusMessage);
    }

    private static TaxonomyManagementTermDto Term(
        string displayName,
        TaxonomyKind kind = TaxonomyKind.Author) =>
        new(
            Guid.NewGuid(),
            kind,
            displayName,
            TaxonomyName.Create(displayName).Normalized,
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            0,
            0);

    private sealed class Fixture
    {
        public Fixture(
            IReadOnlyList<TaxonomyManagementTermDto> selected,
            IReadOnlyList<TaxonomyManagementTermDto> allTerms,
            Guid? preselectedTargetId = null)
        {
            Query = new FakeQueryService(allTerms);
            Maintenance = new FakeMaintenanceService(allTerms);
            ViewModel = new TaxonomyMergeDialogViewModel(
                selected,
                preselectedTargetId,
                Query,
                Maintenance);
        }

        public FakeQueryService Query { get; }

        public FakeMaintenanceService Maintenance { get; }

        public TaxonomyMergeDialogViewModel ViewModel { get; }
    }

    private sealed class FakeQueryService(
        IReadOnlyList<TaxonomyManagementTermDto> terms)
        : ITaxonomyManagementQueryService
    {
        public List<TaxonomyManagementQuery> Queries { get; } = [];

        public Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(
            TaxonomyManagementQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queries.Add(query);
            IReadOnlyList<TaxonomyManagementTermDto> result = terms
                .Where(term => term.Kind == query.Kind)
                .Where(term => query.SearchText is null
                    || term.DisplayName.Contains(
                        query.SearchText,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(term => term.DisplayName)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeMaintenanceService(
        IReadOnlyList<TaxonomyManagementTermDto> terms)
        : ITaxonomyMaintenanceService
    {
        public TaxonomyMaintenanceUndoState UndoState =>
            TaxonomyMaintenanceUndoState.Unavailable;

        public event EventHandler? UndoStateChanged
        {
            add { }
            remove { }
        }

        public int PrepareCallCount { get; private set; }

        public TaskCompletionSource FirstPrepareStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<TaxonomyMergeSelection, CancellationToken,
            Task<TaxonomyMergePrepareResult>>? PrepareHandler { get; set; }

        public TaxonomyMergePreparation? LastPreparation { get; private set; }

        public string? LastPreviewNameOverride { get; private set; }

        public TaxonomyMaintenanceResult ApplyResult { get; set; } =
            new(false, 0, 0, 0, false,
                TaxonomyMaintenanceErrorCodes.PersistenceFailed);

        public async Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
            TaxonomyMergeSelection selection,
            CancellationToken cancellationToken)
        {
            PrepareCallCount++;
            FirstPrepareStarted.TrySetResult();
            TaxonomyMergePrepareResult result = PrepareHandler is null
                ? Prepare(selection)
                : await PrepareHandler(selection, cancellationToken);
            if (result.Succeeded)
            {
                LastPreparation = result.Preparation;
            }

            return result;
        }

        public TaxonomyMergePrepareResult Prepare(
            TaxonomyMergeSelection selection)
        {
            TaxonomyManagementTermDto target = terms.Single(term =>
                term.Id == selection.TargetId);
            TaxonomyManagementTermDto[] sources = selection.SourceIds
                .Select(id => terms.Single(term => term.Id == id))
                .ToArray();
            var owner = new TaxonomyOwnerSnapshot(
                new MetadataOwner(MetadataOwnerKind.InboxItem, Guid.NewGuid()),
                "sample.mp4",
                4,
                new TaxonomyAssignmentSnapshot(
                    sources.Select(term => term.Id).ToArray(),
                    null,
                    []));
            var preparation = new TaxonomyMergePreparation(
                Guid.NewGuid(),
                selection,
                Snapshot(target),
                sources.Select(Snapshot).ToArray(),
                terms.Where(term => term.Kind == selection.Kind)
                    .Select(term => new TaxonomyNameOwnerSnapshot(
                        term.NormalizedName,
                        term.Id))
                    .ToArray(),
                [owner]);
            return new TaxonomyMergePrepareResult(true, preparation, null);
        }

        public TaxonomyMergePreviewDto PreviewMerge(
            TaxonomyMergePreparation preparation,
            string? finalNameOverride)
        {
            LastPreviewNameOverride = finalNameOverride;
            try
            {
                TaxonomyName? finalName = finalNameOverride is null
                    ? null
                    : TaxonomyName.Create(finalNameOverride);
                return TaxonomyMergePreviewBuilder.Build(
                    TaxonomyMergePlanner.Build(preparation, finalName));
            }
            catch (ArgumentException)
            {
                return FailurePreview(TaxonomyMaintenanceErrorCodes.NameInvalid);
            }
            catch (TaxonomyMaintenancePlanningException error)
            {
                return FailurePreview(error.ErrorCode);
            }
        }

        public Task<TaxonomyMaintenanceResult> ApplyMergeAsync(
            TaxonomyMergePreparation preparation,
            string? finalNameOverride,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPreparation = preparation;
            LastPreviewNameOverride = finalNameOverride;
            return Task.FromResult(ApplyResult);
        }

        public Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
            TaxonomyDeleteSelection selection,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TaxonomyMaintenanceResult> DeleteUnusedAsync(
            TaxonomyDeletePreparation preparation,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TaxonomyMaintenanceUndoResult> UndoLastAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void InvalidateUndo()
        {
        }

        private static TaxonomyTermSnapshot Snapshot(
            TaxonomyManagementTermDto term) =>
            new(
                term.Id,
                term.Kind,
                term.DisplayName,
                term.NormalizedName,
                term.UpdatedAtUtc.AddHours(-1),
                term.UpdatedAtUtc);

        private static TaxonomyMergePreviewDto FailurePreview(string errorCode) =>
            new([], 0, 0, false, errorCode);
    }
}
