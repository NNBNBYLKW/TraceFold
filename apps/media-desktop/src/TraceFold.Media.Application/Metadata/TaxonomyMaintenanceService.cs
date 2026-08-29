using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Application.Metadata;

public sealed class TaxonomyMaintenanceService : ITaxonomyMaintenanceService
{
    private readonly ITaxonomyMaintenanceStore _store;
    private readonly IInboxActivityNotifier _inboxNotifier;
    private readonly ILibraryActivityNotifier _libraryNotifier;
    private readonly ILogger<TaxonomyMaintenanceService>? _logger;
    private readonly object _undoLock = new();
    private TaxonomyMaintenanceUndoReceipt? _lastUndoReceipt;

    public TaxonomyMaintenanceService(
        ITaxonomyMaintenanceStore store,
        IInboxActivityNotifier inboxNotifier,
        ILibraryActivityNotifier libraryNotifier,
        ILogger<TaxonomyMaintenanceService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inboxNotifier);
        ArgumentNullException.ThrowIfNull(libraryNotifier);
        _store = store;
        _inboxNotifier = inboxNotifier;
        _libraryNotifier = libraryNotifier;
        _logger = logger;
    }

    public TaxonomyMaintenanceUndoState UndoState
    {
        get
        {
            lock (_undoLock)
            {
                return _lastUndoReceipt switch
                {
                    TaxonomyMergeUndoReceipt merge =>
                        new TaxonomyMaintenanceUndoState(
                            true,
                            merge.Owners.Count,
                            merge.Sources.Count),
                    TaxonomyDeleteUndoReceipt delete =>
                        new TaxonomyMaintenanceUndoState(
                            true,
                            0,
                            delete.Terms.Count),
                    _ => TaxonomyMaintenanceUndoState.Unavailable,
                };
            }
        }
    }

    public event EventHandler? UndoStateChanged;

    public Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return _store.PrepareMergeAsync(selection, cancellationToken);
    }

    public TaxonomyMergePreviewDto PreviewMerge(
        TaxonomyMergePreparation preparation,
        string? finalNameOverride)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        TaxonomyName? finalName;
        try
        {
            finalName = finalNameOverride is null
                ? null
                : TaxonomyName.Create(finalNameOverride);
        }
        catch (ArgumentException)
        {
            return PreviewFailure(
                preparation,
                TaxonomyMaintenanceErrorCodes.NameInvalid);
        }

        try
        {
            return TaxonomyMergePreviewBuilder.Build(
                TaxonomyMergePlanner.Build(preparation, finalName));
        }
        catch (TaxonomyMaintenancePlanningException error)
        {
            return PreviewFailure(preparation, error.ErrorCode);
        }
    }

    public async Task<TaxonomyMaintenanceResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation,
        string? finalNameOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        long started = LogStarted(
            preparation.OperationId,
            preparation.Selection.Kind,
            preparation.Selection.SourceIds.Count,
            deleteCount: 0,
            "merge");
        TaxonomyName? finalName;
        try
        {
            finalName = finalNameOverride is null
                ? null
                : TaxonomyName.Create(finalNameOverride);
        }
        catch (ArgumentException)
        {
            return Finish(
                Failure(TaxonomyMaintenanceErrorCodes.NameInvalid),
                preparation.OperationId,
                preparation.Selection.Kind,
                started,
                "merge");
        }

        TaxonomyMaintenanceStoreResult stored = await _store.ApplyMergeAsync(
            preparation,
            finalName,
            cancellationToken);
        TaxonomyMaintenanceResult result = ToResult(stored);
        if (stored.Succeeded)
        {
            bool targetNameChanged = finalName is not null
                && (!StringComparer.Ordinal.Equals(
                        preparation.Target.DisplayName,
                        finalName.Display)
                    || !StringComparer.Ordinal.Equals(
                        preparation.Target.NormalizedName,
                        finalName.Normalized));
            ReplaceUndoReceipt(stored.UndoReceipt);
            PublishChangedOwners(stored.ChangedOwners, targetNameChanged);
            result = result with { CanUndo = UndoState.IsAvailable };
        }

        return Finish(
            result,
            preparation.OperationId,
            preparation.Selection.Kind,
            started,
            "merge");
    }

    public Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return _store.PrepareDeleteAsync(selection, cancellationToken);
    }

    public async Task<TaxonomyMaintenanceResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        long started = LogStarted(
            preparation.OperationId,
            preparation.Selection.Kind,
            sourceCount: 0,
            preparation.Selection.TermIds.Count,
            "delete");
        TaxonomyMaintenanceStoreResult stored = await _store.DeleteUnusedAsync(
            preparation,
            cancellationToken);
        TaxonomyMaintenanceResult result = ToResult(stored);
        if (stored.Succeeded)
        {
            ReplaceUndoReceipt(stored.UndoReceipt);
            result = result with { CanUndo = UndoState.IsAvailable };
        }

        return Finish(
            result,
            preparation.OperationId,
            preparation.Selection.Kind,
            started,
            "delete");
    }

    public async Task<TaxonomyMaintenanceUndoResult> UndoLastAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaxonomyMaintenanceUndoReceipt? receipt;
        lock (_undoLock)
        {
            receipt = _lastUndoReceipt;
            _lastUndoReceipt = null;
        }

        if (receipt is null)
        {
            return new TaxonomyMaintenanceUndoResult(
                false,
                0,
                0,
                [],
                ReceiptIsRetryable: false,
                TaxonomyMaintenanceErrorCodes.UndoUnavailable);
        }

        UndoStateChanged?.Invoke(this, EventArgs.Empty);
        if (cancellationToken.IsCancellationRequested)
        {
            RestoreReceiptIfEmpty(receipt);
            cancellationToken.ThrowIfCancellationRequested();
        }

        TaxonomyKind kind = ReceiptKind(receipt);
        int sourceCount = receipt is TaxonomyMergeUndoReceipt merge
            ? merge.Sources.Count
            : 0;
        int deleteCount = receipt is TaxonomyDeleteUndoReceipt delete
            ? delete.Terms.Count
            : 0;
        long started = LogStarted(
            receipt.OperationId,
            kind,
            sourceCount,
            deleteCount,
            "undo");
        TaxonomyMaintenanceUndoResult result;
        try
        {
            result = await _store.UndoAsync(receipt, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RestoreReceiptIfEmpty(receipt);
            throw;
        }

        if (result.Succeeded)
        {
            bool targetNameChanged = receipt is TaxonomyMergeUndoReceipt mergeReceipt
                && (!StringComparer.Ordinal.Equals(
                        mergeReceipt.TargetBefore.DisplayName,
                        mergeReceipt.TargetAfter.Display)
                    || !StringComparer.Ordinal.Equals(
                        mergeReceipt.TargetBefore.NormalizedName,
                        mergeReceipt.TargetAfter.Normalized));
            PublishChangedOwners(result.ChangedOwners, targetNameChanged);
        }
        else if (result.ReceiptIsRetryable)
        {
            RestoreReceiptIfEmpty(receipt);
        }

        LogUndoFinished(result, receipt.OperationId, kind, started);
        return result;
    }

    public void InvalidateUndo()
    {
        bool changed = false;
        lock (_undoLock)
        {
            if (_lastUndoReceipt is not null)
            {
                _lastUndoReceipt = null;
                changed = true;
            }
        }

        if (changed)
        {
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static TaxonomyMergePreviewDto PreviewFailure(
        TaxonomyMergePreparation preparation,
        string errorCode)
    {
        try
        {
            TaxonomyMergePreviewDto baseline = TaxonomyMergePreviewBuilder.Build(
                TaxonomyMergePlanner.Build(preparation, finalName: null));
            return baseline with { CanApply = false, ErrorCode = errorCode };
        }
        catch (TaxonomyMaintenancePlanningException)
        {
            return new TaxonomyMergePreviewDto([], 0, 0, false, errorCode);
        }
    }

    private TaxonomyMaintenanceResult ToResult(
        TaxonomyMaintenanceStoreResult stored)
    {
        int inboxCount = stored.ChangedOwners
            .Where(owner => owner.Kind == MetadataOwnerKind.InboxItem)
            .Select(owner => owner.Id)
            .Distinct()
            .Count();
        int libraryCount = stored.ChangedOwners
            .Where(owner => owner.Kind == MetadataOwnerKind.MediaAsset)
            .Select(owner => owner.Id)
            .Distinct()
            .Count();
        return new TaxonomyMaintenanceResult(
            stored.Succeeded,
            inboxCount,
            libraryCount,
            stored.DeletedTermCount,
            UndoState.IsAvailable,
            stored.ErrorCode);
    }

    private TaxonomyMaintenanceResult Failure(string errorCode) =>
        new(false, 0, 0, 0, UndoState.IsAvailable, errorCode);

    private void PublishChangedOwners(
        IReadOnlyList<MetadataOwner> owners,
        bool targetNameChanged = false)
    {
        if (targetNameChanged
            || owners.Any(owner => owner.Kind == MetadataOwnerKind.InboxItem))
        {
            _inboxNotifier.Publish(InboxActivityKind.ItemChanged);
        }

        Guid[] assetIds = owners
            .Where(owner => owner.Kind == MetadataOwnerKind.MediaAsset)
            .Select(owner => owner.Id)
            .Distinct()
            .ToArray();
        if (targetNameChanged || assetIds.Length > 0)
        {
            _libraryNotifier.Publish(
                LibraryActivityKind.TaxonomyChanged,
                assetIds);
        }
    }

    private void ReplaceUndoReceipt(TaxonomyMaintenanceUndoReceipt? receipt)
    {
        lock (_undoLock)
        {
            _lastUndoReceipt = receipt;
        }

        UndoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreReceiptIfEmpty(TaxonomyMaintenanceUndoReceipt receipt)
    {
        bool restored = false;
        lock (_undoLock)
        {
            if (_lastUndoReceipt is null)
            {
                _lastUndoReceipt = receipt;
                restored = true;
            }
        }

        if (restored)
        {
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private long LogStarted(
        Guid operationId,
        TaxonomyKind kind,
        int sourceCount,
        int deleteCount,
        string phase)
    {
        _logger?.LogInformation(
            "Taxonomy maintenance started OperationId {OperationId} Kind {Kind} SourceCount {SourceCount} DeleteCount {DeleteCount} Phase {Phase}",
            operationId,
            kind,
            sourceCount,
            deleteCount,
            phase);
        return Stopwatch.GetTimestamp();
    }

    private TaxonomyMaintenanceResult Finish(
        TaxonomyMaintenanceResult result,
        Guid operationId,
        TaxonomyKind kind,
        long started,
        string phase)
    {
        _logger?.LogInformation(
            "Taxonomy maintenance finished OperationId {OperationId} Kind {Kind} InboxOwnerCount {InboxOwnerCount} LibraryOwnerCount {LibraryOwnerCount} DeletedTermCount {DeletedTermCount} Phase {Phase} DurationMs {DurationMs} Succeeded {Succeeded} ErrorCode {ErrorCode}",
            operationId,
            kind,
            result.ChangedInboxCount,
            result.ChangedLibraryCount,
            result.DeletedTermCount,
            phase,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            result.Succeeded,
            result.ErrorCode);
        return result;
    }

    private void LogUndoFinished(
        TaxonomyMaintenanceUndoResult result,
        Guid operationId,
        TaxonomyKind kind,
        long started)
    {
        _logger?.LogInformation(
            "Taxonomy maintenance undo finished OperationId {OperationId} Kind {Kind} RestoredOwnerCount {RestoredOwnerCount} RestoredTermCount {RestoredTermCount} Phase {Phase} DurationMs {DurationMs} Succeeded {Succeeded} ErrorCode {ErrorCode}",
            operationId,
            kind,
            result.RestoredOwnerCount,
            result.RestoredTermCount,
            "undo",
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            result.Succeeded,
            result.ErrorCode);
    }

    private static TaxonomyKind ReceiptKind(
        TaxonomyMaintenanceUndoReceipt receipt) =>
        receipt switch
        {
            TaxonomyMergeUndoReceipt merge => merge.TargetBefore.Kind,
            TaxonomyDeleteUndoReceipt delete when delete.Terms.Count > 0 =>
                delete.Terms[0].Kind,
            _ => throw new ArgumentException("The undo receipt is invalid."),
        };
}
