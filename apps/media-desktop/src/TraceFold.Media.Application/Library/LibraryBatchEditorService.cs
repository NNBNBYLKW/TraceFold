using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Application.Library;

public sealed class LibraryBatchEditorService : ILibraryBatchEditorService
{
    private readonly ILibraryBatchEditStore _store;
    private readonly ILibraryBatchPreviewService _previewService;
    private readonly ILibraryActivityNotifier _notifier;
    private readonly ILogger<LibraryBatchEditorService>? _logger;
    private readonly object _undoLock = new();
    private LibraryBatchUndoReceipt? _lastUndoReceipt;

    public event EventHandler? UndoStateChanged;

    public LibraryBatchUndoState UndoState
    {
        get
        {
            lock (_undoLock)
            {
                return _lastUndoReceipt is null
                    ? LibraryBatchUndoState.Unavailable
                    : new LibraryBatchUndoState(
                        true,
                        _lastUndoReceipt.Items.Count);
            }
        }
    }

    public LibraryBatchEditorService(
        ILibraryBatchEditStore store,
        ILibraryBatchPreviewService previewService,
        ILibraryActivityNotifier notifier,
        ILogger<LibraryBatchEditorService>? logger = null)
    {
        _store = store;
        _previewService = previewService;
        _notifier = notifier;
        _logger = logger;
        _notifier.Changed += OnLibraryActivityChanged;
    }

    public Task<LibraryBatchPrepareResult> PrepareAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        CancellationToken cancellationToken) =>
        _store.PrepareAsync(selection, cancellationToken);

    public LibraryBatchPreviewDto Preview(
        LibraryBatchEditorDto editor,
        LibraryBatchEditDraft draft) =>
        _previewService.Preview(editor, draft);

    public async Task<LibraryBatchApplyResult> ApplyAsync(
        IReadOnlyList<LibrarySelectionEntry> selection,
        LibraryBatchEditDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(draft);
        Guid operationId = Guid.NewGuid();
        long started = Stopwatch.GetTimestamp();
        string ruleKinds = draft.Naming is null
            ? "none"
            : string.Join(
                ',',
                draft.Naming.Rules.Select(rule => rule.Kind));
        _logger?.LogInformation(
            "Library batch apply started OperationId {OperationId} SelectedCount {SelectedCount} RuleKinds {RuleKinds} Phase {Phase}",
            operationId,
            selection.Count,
            ruleKinds,
            "prepare");

        LibraryBatchPrepareResult prepared = await _store.PrepareAsync(
            selection,
            cancellationToken);
        if (!prepared.Succeeded || prepared.Editor is null)
        {
            return Finish(
                new LibraryBatchApplyResult(
                    false,
                    0,
                    selection.Count,
                    0,
                    UndoState.IsAvailable,
                    prepared.ErrorCode ?? "library.batch.prepare_failed"),
                operationId,
                started);
        }

        LibraryBatchPreviewDto preview;
        try
        {
            preview = _previewService.Preview(prepared.Editor, draft);
        }
        catch (ArgumentException)
        {
            return Finish(
                new LibraryBatchApplyResult(
                    false,
                    0,
                    selection.Count,
                    0,
                    UndoState.IsAvailable,
                    "library.batch.selection_invalid"),
                operationId,
                started);
        }

        if (!preview.CanApply)
        {
            return Finish(
                new LibraryBatchApplyResult(
                    false,
                    0,
                    selection.Count,
                    preview.WarningCount,
                    UndoState.IsAvailable,
                    preview.ErrorCode ?? "library.batch.preview_invalid"),
                operationId,
                started);
        }

        LibraryBatchStoreResult stored = await _store.ApplyAsync(
            selection,
            draft,
            cancellationToken);
        if (!stored.Succeeded)
        {
            return Finish(
                new LibraryBatchApplyResult(
                    false,
                    stored.ChangedCount,
                    stored.UnchangedCount,
                    stored.WarningCount,
                    UndoState.IsAvailable,
                    stored.ErrorCode ?? "library.batch.persistence_failed"),
                operationId,
                started);
        }

        ReplaceUndoReceipt(stored.UndoReceipt);
        if (stored.ChangedCount > 0 && stored.UndoReceipt is not null)
        {
            _notifier.Publish(
                LibraryActivityKind.BatchChanged,
                stored.UndoReceipt.Items.Select(item => item.AssetId).ToArray());
        }

        return Finish(
            new LibraryBatchApplyResult(
                true,
                stored.ChangedCount,
                stored.UnchangedCount,
                stored.WarningCount,
                UndoState.IsAvailable,
                null),
            operationId,
            started);
    }

    public async Task<LibraryBatchUndoResult> UndoLastAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LibraryBatchUndoReceipt? receipt;
        lock (_undoLock)
        {
            receipt = _lastUndoReceipt;
            _lastUndoReceipt = null;
        }

        if (receipt is null)
        {
            return new LibraryBatchUndoResult(
                false,
                0,
                "library.batch.undo.unavailable");
        }

        UndoStateChanged?.Invoke(this, EventArgs.Empty);
        if (cancellationToken.IsCancellationRequested)
        {
            RestoreReceiptIfEmpty(receipt);
            cancellationToken.ThrowIfCancellationRequested();
        }

        LibraryBatchUndoResult result;
        try
        {
            result = await _store.UndoAsync(receipt, cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            RestoreReceiptIfEmpty(receipt);
            throw;
        }

        if (result.Succeeded)
        {
            _notifier.Publish(
                LibraryActivityKind.UndoCompleted,
                receipt.Items.Select(item => item.AssetId).ToArray());
        }

        return result;
    }

    public void InvalidateUndoFor(Guid assetId)
    {
        bool invalidated = false;
        lock (_undoLock)
        {
            if (_lastUndoReceipt?.Items.Any(
                    item => item.AssetId == assetId) == true)
            {
                _lastUndoReceipt = null;
                invalidated = true;
            }
        }

        if (invalidated)
        {
            UndoStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private LibraryBatchApplyResult Finish(
        LibraryBatchApplyResult result,
        Guid operationId,
        long started)
    {
        _logger?.LogInformation(
            "Library batch apply finished OperationId {OperationId} Succeeded {Succeeded} ChangedCount {ChangedCount} UnchangedCount {UnchangedCount} WarningCount {WarningCount} ErrorCode {ErrorCode} DurationMs {DurationMs} Phase {Phase}",
            operationId,
            result.Succeeded,
            result.ChangedCount,
            result.UnchangedCount,
            result.WarningCount,
            result.ErrorCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            "finish");
        return result;
    }

    private void OnLibraryActivityChanged(
        object? sender,
        LibraryActivityEventArgs args)
    {
        if (args.Kind is not (LibraryActivityKind.SingleMetadataChanged
            or LibraryActivityKind.TaxonomyChanged))
        {
            return;
        }

        foreach (Guid assetId in args.AssetIds)
        {
            InvalidateUndoFor(assetId);
        }
    }

    private void ReplaceUndoReceipt(LibraryBatchUndoReceipt? receipt)
    {
        lock (_undoLock)
        {
            _lastUndoReceipt = receipt;
        }

        UndoStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreReceiptIfEmpty(LibraryBatchUndoReceipt receipt)
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
}
