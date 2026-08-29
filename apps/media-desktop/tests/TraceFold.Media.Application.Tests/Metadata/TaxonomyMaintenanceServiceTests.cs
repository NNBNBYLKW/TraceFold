using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using Xunit;

namespace TraceFold.Media.Application.Tests.Metadata;

public sealed class TaxonomyMaintenanceServiceTests
{
    private static readonly Guid InboxId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid AssetId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Successful_merge_replaces_receipt_and_publishes_both_owner_kinds()
    {
        TaxonomyMergePreparation preparation = Preparation();
        TaxonomyMergeUndoReceipt receipt = MergeReceipt(preparation);
        var store = new RecordingMaintenanceStore
        {
            MergeResult = Success(
                receipt,
                new MetadataOwner(MetadataOwnerKind.InboxItem, InboxId),
                new MetadataOwner(MetadataOwnerKind.MediaAsset, AssetId))
        };
        var inbox = new RecordingInboxActivityNotifier();
        var library = new RecordingLibraryActivityNotifier();
        var service = new TaxonomyMaintenanceService(store, inbox, library);

        TaxonomyMaintenanceResult result = await service.ApplyMergeAsync(
            preparation,
            finalNameOverride: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(service.UndoState.IsAvailable);
        Assert.Equal(1, result.ChangedInboxCount);
        Assert.Equal(1, result.ChangedLibraryCount);
        Assert.Equal([InboxActivityKind.ItemChanged], inbox.Published);
        LibraryActivityEventArgs changed = Assert.Single(library.Published);
        Assert.Equal(LibraryActivityKind.TaxonomyChanged, changed.Kind);
        Assert.Equal([AssetId], changed.AssetIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Preview_invalid_name_retains_rows_and_returns_stable_error(
        string invalidName)
    {
        TaxonomyMergePreparation preparation = Preparation();
        var service = Service(new RecordingMaintenanceStore());

        TaxonomyMergePreviewDto preview = service.PreviewMerge(
            preparation,
            invalidName);

        Assert.False(preview.CanApply);
        Assert.Equal(TaxonomyMaintenanceErrorCodes.NameInvalid, preview.ErrorCode);
        Assert.Single(preview.Rows);
    }

    [Fact]
    public async Task Failed_new_operation_preserves_the_prior_receipt()
    {
        TaxonomyMergePreparation preparation = Preparation();
        TaxonomyMergeUndoReceipt receipt = MergeReceipt(preparation);
        var store = new RecordingMaintenanceStore();
        store.MergeResults.Enqueue(Success(receipt));
        store.MergeResults.Enqueue(new TaxonomyMaintenanceStoreResult(
            false,
            [],
            0,
            null,
            TaxonomyMaintenanceErrorCodes.PersistenceFailed));
        var service = Service(store);

        await service.ApplyMergeAsync(
            preparation,
            null,
            CancellationToken.None);
        TaxonomyMaintenanceResult failed = await service.ApplyMergeAsync(
            preparation,
            null,
            CancellationToken.None);
        TaxonomyMaintenanceUndoResult undone = await service.UndoLastAsync(
            CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.True(undone.Succeeded);
        Assert.Same(receipt, store.LastUndoReceipt);
    }

    [Fact]
    public async Task Successful_delete_replaces_merge_receipt_and_invalidate_clears_it()
    {
        TaxonomyMergePreparation merge = Preparation();
        TaxonomyMergeUndoReceipt mergeReceipt = MergeReceipt(merge);
        TaxonomyDeletePreparation delete = DeletePreparation();
        var deleteReceipt = new TaxonomyDeleteUndoReceipt(
            delete.OperationId,
            delete.Terms);
        var store = new RecordingMaintenanceStore
        {
            MergeResult = Success(mergeReceipt),
            DeleteResult = Success(deleteReceipt)
        };
        var service = Service(store);

        await service.ApplyMergeAsync(merge, null, CancellationToken.None);
        await service.DeleteUnusedAsync(delete, CancellationToken.None);
        TaxonomyMaintenanceUndoResult undone = await service.UndoLastAsync(
            CancellationToken.None);

        Assert.True(undone.Succeeded);
        Assert.Same(deleteReceipt, store.LastUndoReceipt);

        await service.ApplyMergeAsync(merge, null, CancellationToken.None);
        service.InvalidateUndo();
        Assert.False(service.UndoState.IsAvailable);
    }

    [Fact]
    public async Task Semantic_conflict_consumes_receipt_but_transient_failure_restores_it()
    {
        TaxonomyMergePreparation preparation = Preparation();
        TaxonomyMergeUndoReceipt receipt = MergeReceipt(preparation);
        var conflictStore = new RecordingMaintenanceStore
        {
            MergeResult = Success(receipt),
            UndoResult = new TaxonomyMaintenanceUndoResult(
                false,
                0,
                0,
                [],
                ReceiptIsRetryable: false,
                TaxonomyMaintenanceErrorCodes.UndoConflict)
        };
        var conflictService = Service(conflictStore);
        await conflictService.ApplyMergeAsync(
            preparation,
            null,
            CancellationToken.None);

        TaxonomyMaintenanceUndoResult conflict =
            await conflictService.UndoLastAsync(CancellationToken.None);

        Assert.False(conflict.Succeeded);
        Assert.False(conflictService.UndoState.IsAvailable);

        var retryStore = new RecordingMaintenanceStore
        {
            MergeResult = Success(receipt),
            UndoResult = new TaxonomyMaintenanceUndoResult(
                false,
                0,
                0,
                [],
                ReceiptIsRetryable: true,
                TaxonomyMaintenanceErrorCodes.PersistenceFailed)
        };
        var retryService = Service(retryStore);
        await retryService.ApplyMergeAsync(
            preparation,
            null,
            CancellationToken.None);

        TaxonomyMaintenanceUndoResult retry =
            await retryService.UndoLastAsync(CancellationToken.None);

        Assert.False(retry.Succeeded);
        Assert.True(retryService.UndoState.IsAvailable);
    }

    [Fact]
    public async Task Notifications_are_coalesced_and_asset_ids_are_distinct()
    {
        TaxonomyMergePreparation preparation = Preparation();
        TaxonomyMergeUndoReceipt receipt = MergeReceipt(preparation);
        var store = new RecordingMaintenanceStore
        {
            MergeResult = Success(
                receipt,
                new MetadataOwner(MetadataOwnerKind.InboxItem, InboxId),
                new MetadataOwner(MetadataOwnerKind.InboxItem, Guid.NewGuid()),
                new MetadataOwner(MetadataOwnerKind.MediaAsset, AssetId),
                new MetadataOwner(MetadataOwnerKind.MediaAsset, AssetId))
        };
        var inbox = new RecordingInboxActivityNotifier();
        var library = new RecordingLibraryActivityNotifier();
        var service = new TaxonomyMaintenanceService(store, inbox, library);

        await service.ApplyMergeAsync(
            preparation,
            null,
            CancellationToken.None);

        Assert.Single(inbox.Published);
        Assert.Equal([AssetId], Assert.Single(library.Published).AssetIds);
    }

    private static TaxonomyMaintenanceService Service(
        RecordingMaintenanceStore store) =>
        new(
            store,
            new RecordingInboxActivityNotifier(),
            new RecordingLibraryActivityNotifier());

    internal static TaxonomyMergePreparation Preparation(
        string targetName = "Target",
        string sourceName = "Source",
        string visibleName = "Visible file")
    {
        TaxonomyTermSnapshot target = Term(TaxonomyKind.Author, targetName);
        TaxonomyTermSnapshot source = Term(TaxonomyKind.Author, sourceName);
        var before = new TaxonomyAssignmentSnapshot([source.Id], null, []);
        var owner = new TaxonomyOwnerSnapshot(
            new MetadataOwner(MetadataOwnerKind.InboxItem, InboxId),
            visibleName,
            1,
            before);
        return new TaxonomyMergePreparation(
            Guid.NewGuid(),
            TaxonomyMergeSelection.Create(
                TaxonomyKind.Author,
                [source.Id],
                target.Id),
            target,
            [source],
            [
                new TaxonomyNameOwnerSnapshot(target.NormalizedName, target.Id),
                new TaxonomyNameOwnerSnapshot(source.NormalizedName, source.Id),
            ],
            [owner]);
    }

    internal static TaxonomyMaintenanceStoreResult Success(
        TaxonomyMaintenanceUndoReceipt receipt,
        params MetadataOwner[] owners) =>
        new(true, owners, receipt is TaxonomyDeleteUndoReceipt delete
            ? delete.Terms.Count
            : ((TaxonomyMergeUndoReceipt)receipt).Sources.Count, receipt, null);

    private static TaxonomyDeletePreparation DeletePreparation()
    {
        TaxonomyTermSnapshot term = Term(TaxonomyKind.Tag, "Unused");
        return new TaxonomyDeletePreparation(
            Guid.NewGuid(),
            TaxonomyDeleteSelection.Create(TaxonomyKind.Tag, [term.Id]),
            [term],
            []);
    }

    private static TaxonomyMergeUndoReceipt MergeReceipt(
        TaxonomyMergePreparation preparation) =>
        new(
            preparation.OperationId,
            preparation.Target,
            TaxonomyName.Create(preparation.Target.DisplayName),
            preparation.Sources,
            []);

    private static TaxonomyTermSnapshot Term(TaxonomyKind kind, string name)
    {
        TaxonomyName taxonomyName = TaxonomyName.Create(name);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        return new TaxonomyTermSnapshot(
            Guid.NewGuid(),
            kind,
            taxonomyName.Display,
            taxonomyName.Normalized,
            now,
            now);
    }

    internal sealed class RecordingMaintenanceStore : ITaxonomyMaintenanceStore
    {
        public TaxonomyMaintenanceStoreResult MergeResult { get; init; } =
            new(false, [], 0, null, "not_configured");

        public Queue<TaxonomyMaintenanceStoreResult> MergeResults { get; } = [];

        public TaxonomyMaintenanceStoreResult DeleteResult { get; init; } =
            new(false, [], 0, null, "not_configured");

        public TaxonomyMaintenanceUndoResult UndoResult { get; init; } =
            new(true, 0, 0, [], false, null);

        public TaxonomyMaintenanceUndoReceipt? LastUndoReceipt { get; private set; }

        public Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
            TaxonomyMergeSelection selection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyMergePrepareResult(
                false,
                null,
                "not_configured"));

        public Task<TaxonomyMaintenanceStoreResult> ApplyMergeAsync(
            TaxonomyMergePreparation preparation,
            TaxonomyName? finalName,
            CancellationToken cancellationToken) =>
            Task.FromResult(MergeResults.Count > 0
                ? MergeResults.Dequeue()
                : MergeResult);

        public Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
            TaxonomyDeleteSelection selection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TaxonomyDeletePrepareResult(
                false,
                null,
                "not_configured"));

        public Task<TaxonomyMaintenanceStoreResult> DeleteUnusedAsync(
            TaxonomyDeletePreparation preparation,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeleteResult);

        public Task<TaxonomyMaintenanceUndoResult> UndoAsync(
            TaxonomyMaintenanceUndoReceipt receipt,
            CancellationToken cancellationToken)
        {
            LastUndoReceipt = receipt;
            return Task.FromResult(UndoResult);
        }
    }

    private sealed class RecordingInboxActivityNotifier : IInboxActivityNotifier
    {
        public event EventHandler<InboxActivityEventArgs>? Changed;

        public List<InboxActivityKind> Published { get; } = [];

        public void Publish(InboxActivityKind kind)
        {
            Published.Add(kind);
            Changed?.Invoke(this, new InboxActivityEventArgs(kind));
        }

        public Task WaitForCandidateAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class RecordingLibraryActivityNotifier
        : ILibraryActivityNotifier
    {
        public event EventHandler<LibraryActivityEventArgs>? Changed;

        public List<LibraryActivityEventArgs> Published { get; } = [];

        public void Publish(
            LibraryActivityKind kind,
            IReadOnlyList<Guid> assetIds)
        {
            var args = new LibraryActivityEventArgs(kind, assetIds);
            Published.Add(args);
            Changed?.Invoke(this, args);
        }
    }
}
