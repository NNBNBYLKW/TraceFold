using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public enum LibraryBatchEditTab
{
    Metadata,
    Naming
}

public sealed partial class LibraryBatchEditDialogViewModel : ObservableObject
{
    private readonly ILibraryBatchEditorService _editorService;
    private readonly IReadOnlyList<LibrarySelectionEntry> _selection;

    public LibraryBatchEditDialogViewModel(
        ILibraryBatchEditorService editorService,
        ILibraryBatchPreviewService previewService,
        INamingPresetService presetService,
        ITaxonomySearchService taxonomy,
        LibraryBatchEditorDto editor)
    {
        ArgumentNullException.ThrowIfNull(editorService);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(presetService);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(editor);
        if (editor.Assets.Count == 0)
        {
            throw new ArgumentException("The formal selection cannot be empty.", nameof(editor));
        }

        _editorService = editorService;
        _selection = editor.Assets
            .OrderBy(asset => asset.Selection.Ordinal)
            .Select(asset => asset.Selection)
            .ToArray();
        Metadata = new LibraryMetadataDraftViewModel(taxonomy);
        Metadata.Load(editor);
        Naming = new LibraryNamingDraftViewModel(
            presetService,
            previewService,
            editor,
            MetadataDraftOrNull,
            existingTermsProvider: () => Metadata.ExistingTermSnapshots);
        Metadata.DraftChanged += OnDraftChanged;
        Naming.PreviewChanged += OnPreviewChanged;
        ApplyCommand = new AsyncRelayCommand(
            ApplyAsync,
            () => CanApply);
        Naming.RefreshPreview();
    }

    public LibraryMetadataDraftViewModel Metadata { get; }

    public LibraryNamingDraftViewModel Naming { get; }

    public IAsyncRelayCommand ApplyCommand { get; }

    public int SelectedCount => _selection.Count;

    [ObservableProperty]
    public partial LibraryBatchEditTab SelectedTab { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFocusedProblem { get; private set; }

    [ObservableProperty]
    public partial bool IsSubmitting { get; private set; }

    [ObservableProperty]
    public partial LibraryBatchApplyResult? LastResult { get; private set; }

    public bool CanApply =>
        !IsSubmitting
        && Metadata.TryBuildDraft(out _, out _)
        && Naming.CanApply;

    public int WarningCount => Naming.WarningCount;

    public int ErrorCount => Naming.ErrorCount;

    public IReadOnlyList<LibraryBatchPreviewRowDto> PreviewRows =>
        Naming.PreviewRows;

    public static string ErrorMessage(string? errorCode) => errorCode switch
    {
        "library.batch.version_conflict" =>
            "选择中的资料已变化，请刷新后重试。",
        "library.batch.selection_missing" =>
            "选择中的资料已不存在，请刷新后重试。",
        "library.batch.preview_invalid" =>
            "当前预览存在错误，请先修正后再应用。",
        "library.batch.persistence_failed" =>
            "保存失败，当前输入已保留，可直接重试。",
        "naming.empty" => "生成的显示名称不能为空。",
        "naming.too_long" => "生成的显示名称过长。",
        "naming.placeholder_missing" => "部分文件缺少模板所需资料。",
        "naming.sequence_invalid" => "序号参数无效。",
        "naming.replace_search_empty" => "替换规则必须填写查找内容。",
        "naming.remove_text_empty" => "移除规则必须填写要移除的内容。",
        "naming.placeholder_unknown" => "命名模板包含不支持的占位符。",
        "library.batch.no_changes" => "当前设置不会产生任何变化。",
        _ => "批量修改未完成，当前输入已保留，可直接重试。"
    };

    public Task ApplyDraftAsync(CancellationToken cancellationToken) =>
        ApplyAsync(cancellationToken);

    public void ReportInputError(string message)
    {
        StatusMessage = string.IsNullOrWhiteSpace(message)
            ? "输入无效，请检查后重试。"
            : message;
        HasFocusedProblem = true;
    }

    partial void OnIsSubmittingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildDraft(out LibraryBatchEditDraft? draft, out string? error))
        {
            StatusMessage = error ?? "当前设置无法应用。";
            HasFocusedProblem = true;
            return;
        }

        IsSubmitting = true;
        HasFocusedProblem = false;
        try
        {
            LibraryBatchApplyResult result = await _editorService.ApplyAsync(
                _selection,
                draft!,
                cancellationToken);
            LastResult = result;
            if (result.Succeeded)
            {
                StatusMessage =
                    $"已修改 {result.ChangedCount} 项，{result.UnchangedCount} 项无变化。";
                return;
            }

            StatusMessage = ErrorMessage(result.ErrorCode);
            HasFocusedProblem = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "批量修改已取消，当前输入已保留。";
        }
        catch (Exception)
        {
            StatusMessage = ErrorMessage("library.batch.persistence_failed");
            HasFocusedProblem = true;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private bool TryBuildDraft(
        out LibraryBatchEditDraft? draft,
        out string? error)
    {
        if (!Metadata.TryBuildDraft(
                out BatchMetadataDraft? metadata,
                out error))
        {
            draft = null;
            return false;
        }

        if (!Naming.TryBuildDefinition(
                out NamingDefinition? naming,
                out string? namingError))
        {
            draft = null;
            error = ErrorMessage(namingError);
            return false;
        }

        if (metadata is null && naming is null)
        {
            draft = null;
            error = ErrorMessage("library.batch.no_changes");
            return false;
        }

        Naming.RefreshPreview();
        if (Naming.LastPreview?.CanApply != true)
        {
            draft = null;
            error = ErrorMessage(
                Naming.FirstErrorCode ?? Naming.LastPreview?.ErrorCode);
            return false;
        }

        draft = LibraryBatchEditDraft.Create(
            metadata,
            naming,
            Metadata.ExistingTermSnapshots);
        error = null;
        return true;
    }

    private BatchMetadataDraft? MetadataDraftOrNull()
    {
        Metadata.TryBuildDraft(out BatchMetadataDraft? draft, out _);
        return draft;
    }

    private void OnDraftChanged(object? sender, EventArgs e)
    {
        Naming.RefreshPreview();
        RefreshCoordinatorState();
    }

    private void OnPreviewChanged(object? sender, EventArgs e) =>
        RefreshCoordinatorState();

    private void RefreshCoordinatorState()
    {
        string? issueCode = Naming.FirstErrorCode;
        if (issueCode is not null)
        {
            StatusMessage = ErrorMessage(issueCode);
        }
        else if (!HasFocusedProblem)
        {
            StatusMessage = string.Empty;
        }

        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(PreviewRows));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}
