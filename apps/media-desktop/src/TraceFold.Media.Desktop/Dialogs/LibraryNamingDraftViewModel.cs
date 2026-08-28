using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class LibraryNamingDraftViewModel : ObservableObject
{
    private readonly INamingPresetService _presets;
    private readonly ILibraryBatchPreviewService _preview;
    private readonly LibraryBatchEditorDto _editor;
    private readonly Func<BatchMetadataDraft?>? _metadataProvider;
    private readonly Func<IReadOnlyList<TaxonomyTermDto>>?
        _existingTermsProvider;
    private readonly Guid _dialogId;
    private int _nextAutomationIndex = 1;

    public LibraryNamingDraftViewModel(
        INamingPresetService presets,
        ILibraryBatchPreviewService preview,
        LibraryBatchEditorDto editor,
        Func<BatchMetadataDraft?>? metadataProvider = null,
        Guid? dialogId = null,
        Func<IReadOnlyList<TaxonomyTermDto>>? existingTermsProvider = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(editor);
        _presets = presets;
        _preview = preview;
        _editor = editor;
        _metadataProvider = metadataProvider;
        _existingTermsProvider = existingTermsProvider;
        _dialogId = dialogId ?? Guid.NewGuid();
        if (_dialogId == Guid.Empty)
        {
            throw new ArgumentException("A valid dialog id is required.", nameof(dialogId));
        }

        LoadPresetsCommand = new AsyncRelayCommand(
            LoadPresetsAsync,
            () => !IsPresetBusy);
        SavePresetCommand = new AsyncRelayCommand(
            SavePresetFromInputAsync,
            CanSavePreset);
        OverwritePresetCommand = new AsyncRelayCommand(
            OverwritePresetFromInputAsync,
            () => CanSavePreset() && RequiresPresetOverwrite);
        DeletePresetCommand = new AsyncRelayCommand(
            DeleteSelectedPresetAsync,
            () => !IsPresetBusy && SelectedPreset is not null);
        LoadPresetCommand = new RelayCommand<NamingPresetDto?>(
            preset =>
            {
                if (preset is not null)
                {
                    LoadPreset(preset);
                }
            },
            preset => !IsPresetBusy && preset?.CanExecute == true);
    }

    public event EventHandler? PreviewChanged;

    public ObservableCollection<NamingRuleRowViewModel> Rules { get; } = [];

    public ObservableCollection<NamingPresetDto> Presets { get; } = [];

    public ObservableCollection<LibraryBatchPreviewRowDto> PreviewRows { get; } = [];

    public IAsyncRelayCommand LoadPresetsCommand { get; }

    public IAsyncRelayCommand SavePresetCommand { get; }

    public IAsyncRelayCommand OverwritePresetCommand { get; }

    public IAsyncRelayCommand DeletePresetCommand { get; }

    public IRelayCommand<NamingPresetDto?> LoadPresetCommand { get; }

    [ObservableProperty]
    public partial NamingSource Source { get; set; } = NamingSource.ActualFilename;

    [ObservableProperty]
    public partial NamingRuleRowViewModel? SelectedRule { get; set; }

    [ObservableProperty]
    public partial NamingPresetDto? SelectedPreset { get; set; }

    [ObservableProperty]
    public partial LibraryBatchPreviewDto? LastPreview { get; private set; }

    [ObservableProperty]
    public partial int WarningCount { get; private set; }

    [ObservableProperty]
    public partial int ErrorCount { get; private set; }

    [ObservableProperty]
    public partial string? FirstErrorCode { get; private set; }

    [ObservableProperty]
    public partial string? PresetErrorCode { get; private set; }

    [ObservableProperty]
    public partial bool RequiresPresetOverwrite { get; private set; }

    [ObservableProperty]
    public partial bool IsPresetBusy { get; private set; }

    [ObservableProperty]
    public partial string PresetName { get; set; } = string.Empty;

    public bool CanApply => LastPreview?.CanApply == true;

    public NamingRuleRowViewModel AddRule(INamingRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var row = new NamingRuleRowViewModel(
            _dialogId,
            _nextAutomationIndex++,
            Rules.Count + 1,
            rule);
        row.RuleChanged += OnRuleChanged;
        Rules.Add(row);
        SelectedRule = row;
        NotifyPresetCommands();
        RefreshPreview();
        return row;
    }

    public void DeleteRule(NamingRuleRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!Rules.Remove(row))
        {
            return;
        }

        row.RuleChanged -= OnRuleChanged;
        if (ReferenceEquals(SelectedRule, row))
        {
            SelectedRule = Rules.LastOrDefault();
        }

        UpdateDisplayIndexes();
        NotifyPresetCommands();
        RefreshPreview();
    }

    public void MoveRuleUp(NamingRuleRowViewModel row)
    {
        MoveRule(row, -1);
    }

    public void MoveRuleDown(NamingRuleRowViewModel row)
    {
        MoveRule(row, 1);
    }

    public void UpdateRule(
        NamingRuleRowViewModel row,
        INamingRule rule)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!Rules.Contains(row))
        {
            throw new ArgumentException("The rule is not in this draft.", nameof(row));
        }

        row.UpdateRule(rule);
    }

    public bool TryBuildDefinition(
        out NamingDefinition? definition,
        out string? error)
    {
        if (Rules.Count == 0)
        {
            definition = null;
            error = null;
            return true;
        }

        try
        {
            definition = NamingDefinition.Create(
                Source,
                Rules.Select(row => row.Rule).ToArray());
            error = null;
            return true;
        }
        catch (ArgumentException)
        {
            definition = null;
            error = "naming.rule_invalid";
            return false;
        }
    }

    public void RefreshPreview()
    {
        BatchMetadataDraft? metadata = _metadataProvider?.Invoke();
        if (!TryBuildDefinition(out NamingDefinition? naming, out string? error))
        {
            ClearPreview(error);
            return;
        }

        if (metadata is null && naming is null)
        {
            ClearPreview(null);
            return;
        }

        try
        {
            LibraryBatchEditDraft draft =
                LibraryBatchEditDraft.Create(
                    metadata,
                    naming,
                    _existingTermsProvider?.Invoke());
            LibraryBatchPreviewDto result = _preview.Preview(_editor, draft);
            LastPreview = result;
            Replace(PreviewRows, result.Rows);
            WarningCount = result.WarningCount;
            ErrorCount = result.ErrorCount;
            FirstErrorCode = result.Rows
                .OrderBy(row => row.Ordinal)
                .SelectMany(row => row.Issues)
                .FirstOrDefault(issue =>
                    issue.Severity == NamingIssueSeverity.Error)
                ?.Code
                ?? (result.ErrorCode == "library.batch.preview_invalid"
                    ? "library.batch.preview_invalid"
                    : null);
            NotifyPreviewChanged();
        }
        catch (ArgumentException)
        {
            ClearPreview("library.batch.preview_invalid");
        }
    }

    public async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
        IsPresetBusy = true;
        try
        {
            IReadOnlyList<NamingPresetDto> presets =
                await _presets.ListAsync(cancellationToken);
            Replace(Presets, presets);
            PresetErrorCode = null;
        }
        finally
        {
            IsPresetBusy = false;
        }
    }

    public bool LoadPreset(NamingPresetDto preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!preset.CanExecute || preset.Definition is null)
        {
            PresetErrorCode = preset.ErrorCode
                ?? "naming.preset.definition_invalid";
            return false;
        }

        ClearRules();
        Source = preset.Definition.Source;
        foreach (INamingRule rule in preset.Definition.Rules)
        {
            AddRule(rule);
        }

        SelectedPreset = preset;
        PresetName = preset.Name;
        PresetErrorCode = null;
        RequiresPresetOverwrite = false;
        RefreshPreview();
        return true;
    }

    public async Task<NamingPresetSaveResult> SavePresetAsync(
        string name,
        bool overwriteExisting,
        CancellationToken cancellationToken)
    {
        if (!TryBuildDefinition(
                out NamingDefinition? definition,
                out string? definitionError)
            || definition is null)
        {
            PresetErrorCode = definitionError
                ?? "naming.preset.definition_invalid";
            RequiresPresetOverwrite = false;
            return new(false, null, PresetErrorCode);
        }

        IsPresetBusy = true;
        try
        {
            Guid? id = SelectedPreset is not null
                && string.Equals(
                    SelectedPreset.Name,
                    name.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                    ? SelectedPreset.Id
                    : null;
            NamingPresetSaveResult result = await _presets.SaveAsync(
                new NamingPresetSaveRequest(
                    id,
                    name,
                    definition,
                    overwriteExisting),
                cancellationToken);
            PresetErrorCode = result.ErrorCode;
            RequiresPresetOverwrite =
                result.ErrorCode == "naming.preset.name_conflict";
            if (result.Succeeded && result.Preset is not null)
            {
                UpsertPreset(result.Preset);
                SelectedPreset = result.Preset;
                PresetName = result.Preset.Name;
            }

            return result;
        }
        finally
        {
            IsPresetBusy = false;
        }
    }

    public async Task<NamingPresetDeleteResult> DeleteSelectedPresetAsync(
        CancellationToken cancellationToken)
    {
        NamingPresetDto? selected = SelectedPreset;
        if (selected is null)
        {
            PresetErrorCode = "naming.preset.not_selected";
            return new(false, PresetErrorCode);
        }

        IsPresetBusy = true;
        try
        {
            NamingPresetDeleteResult result = await _presets.DeleteAsync(
                selected.Id,
                cancellationToken);
            PresetErrorCode = result.ErrorCode;
            if (result.Succeeded)
            {
                Presets.Remove(selected);
                SelectedPreset = null;
            }

            return result;
        }
        finally
        {
            IsPresetBusy = false;
        }
    }

    partial void OnSourceChanged(NamingSource value) => RefreshPreview();

    partial void OnPresetNameChanged(string value) => NotifyPresetCommands();

    partial void OnSelectedPresetChanged(NamingPresetDto? value) =>
        NotifyPresetCommands();

    partial void OnRequiresPresetOverwriteChanged(bool value) =>
        NotifyPresetCommands();

    partial void OnIsPresetBusyChanged(bool value) => NotifyPresetCommands();

    private void MoveRule(NamingRuleRowViewModel row, int offset)
    {
        ArgumentNullException.ThrowIfNull(row);
        int current = Rules.IndexOf(row);
        int target = current + offset;
        if (current < 0 || target < 0 || target >= Rules.Count)
        {
            return;
        }

        Rules.Move(current, target);
        UpdateDisplayIndexes();
        RefreshPreview();
    }

    private void OnRuleChanged(object? sender, EventArgs e) => RefreshPreview();

    private void UpdateDisplayIndexes()
    {
        for (int index = 0; index < Rules.Count; index++)
        {
            Rules[index].DisplayIndex = index + 1;
        }
    }

    private void ClearRules()
    {
        foreach (NamingRuleRowViewModel row in Rules)
        {
            row.RuleChanged -= OnRuleChanged;
        }

        Rules.Clear();
        SelectedRule = null;
        NotifyPresetCommands();
    }

    private void ClearPreview(string? errorCode)
    {
        LastPreview = null;
        PreviewRows.Clear();
        WarningCount = 0;
        ErrorCount = errorCode is null ? 0 : 1;
        FirstErrorCode = errorCode;
        NotifyPreviewChanged();
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpsertPreset(NamingPresetDto preset)
    {
        NamingPresetDto? existing = Presets.FirstOrDefault(value =>
            value.Id == preset.Id);
        if (existing is null)
        {
            Presets.Add(preset);
            return;
        }

        Presets[Presets.IndexOf(existing)] = preset;
    }

    private Task SavePresetFromInputAsync(CancellationToken cancellationToken) =>
        SavePresetAsync(PresetName, false, cancellationToken);

    private Task OverwritePresetFromInputAsync(
        CancellationToken cancellationToken) =>
        SavePresetAsync(PresetName, true, cancellationToken);

    private bool CanSavePreset() =>
        !IsPresetBusy
        && Rules.Count > 0
        && !string.IsNullOrWhiteSpace(PresetName);

    private void NotifyPresetCommands()
    {
        LoadPresetsCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        OverwritePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
        LoadPresetCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }
}
