using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TraceFold.Media.Application.Library;

public sealed class LibraryBatchEditorService : ILibraryBatchEditorService
{
    private readonly ILibraryBatchEditStore _store;
    private readonly ILibraryBatchPreviewService _previewService;
    private readonly ILibraryActivityNotifier _notifier;
    private readonly ILogger<LibraryBatchEditorService>? _logger;
    private LibraryBatchUndoReceipt? _lastUndoReceipt;

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
                    _lastUndoReceipt is not null,
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
                    _lastUndoReceipt is not null,
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
                    _lastUndoReceipt is not null,
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
                    _lastUndoReceipt is not null,
                    stored.ErrorCode ?? "library.batch.persistence_failed"),
                operationId,
                started);
        }

        _lastUndoReceipt = stored.UndoReceipt;
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
                _lastUndoReceipt is not null,
                null),
            operationId,
            started);
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
        if (args.Kind != LibraryActivityKind.SingleMetadataChanged
            || _lastUndoReceipt is null)
        {
            return;
        }

        HashSet<Guid> changed = args.AssetIds.ToHashSet();
        if (_lastUndoReceipt.Items.Any(item => changed.Contains(item.AssetId)))
        {
            _lastUndoReceipt = null;
        }
    }
}
