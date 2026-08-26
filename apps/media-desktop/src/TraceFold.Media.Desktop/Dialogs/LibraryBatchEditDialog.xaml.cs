using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class LibraryBatchEditDialog : ContentDialog
{
    private CancellationToken _operationToken;
    private bool _synchronizingRuleEditor;

    public LibraryBatchEditDialog(LibraryBatchEditDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public LibraryBatchEditDialogViewModel ViewModel { get; }

    public LibraryBatchEditDialogResult Result { get; private set; } =
        new(false, null);

    public async Task<LibraryBatchEditDialogResult> ShowEditorAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _operationToken = cancellationToken;
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => DispatcherQueue.TryEnqueue(Hide));
        await ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return Result;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        MetadataFirstInput.Focus(FocusState.Programmatic);
        try
        {
            await ViewModel.Naming.LoadPresetsAsync(_operationToken);
        }
        catch (OperationCanceledException) when (_operationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ViewModel.ReportInputError(error.Message);
        }
    }

    private void OnEditorTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.SelectedTab = EditorTabs.SelectedIndex == 1
            ? LibraryBatchEditTab.Naming
            : LibraryBatchEditTab.Metadata;
    }

    private void OnMetadataModeSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        int selected = Math.Max(0, MetadataModeList.SelectedIndex);
        ViewModel.Metadata.Mode = selected switch
        {
            1 => BatchMetadataMode.Remove,
            2 => BatchMetadataMode.Replace,
            _ => BatchMetadataMode.Add
        };
        MetadataAddPanel.Visibility = selected == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        MetadataRemovePanel.Visibility = selected == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        MetadataReplacePanel.Visibility = selected == 2
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnPickerTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput
            || sender.Tag is not BatchTaxonomyPickerViewModel picker)
        {
            return;
        }

        try
        {
            await picker.SearchAsync(sender.Text, _operationToken);
        }
        catch (OperationCanceledException) when (_operationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ViewModel.ReportInputError(error.Message);
        }
    }

    private async void OnPickerQuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.Tag is not BatchTaxonomyPickerViewModel picker)
        {
            return;
        }

        string value = args.ChosenSuggestion is TaxonomyTermDto selected
            ? selected.DisplayName
            : args.QueryText;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            picker.InputText = value;
            await picker.CommitInputAsync(_operationToken);
        }
        catch (OperationCanceledException) when (_operationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ViewModel.ReportInputError(error.Message);
        }
    }

    private void OnRemoveChoiceClicked(object sender, RoutedEventArgs args)
    {
        _ = args;
        if (sender is not Button { Tag: BatchTaxonomyReference value })
        {
            return;
        }

        MetadataPickerFor(value.Kind)?.Remove(value);
    }

    private BatchTaxonomyPickerViewModel? MetadataPickerFor(
        TaxonomyKind kind) => (ViewModel.Metadata.Mode, kind) switch
    {
        (BatchMetadataMode.Add, TaxonomyKind.Author) =>
            ViewModel.Metadata.AddAuthorPicker,
        (BatchMetadataMode.Add, TaxonomyKind.Tag) =>
            ViewModel.Metadata.AddTagPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Author) =>
            ViewModel.Metadata.ReplaceAuthorPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Series) =>
            ViewModel.Metadata.ReplaceSeriesPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Tag) =>
            ViewModel.Metadata.ReplaceTagPicker,
        _ => null
    };

    private void OnNamingSourceChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Naming.Source = NamingSourceSelector.SelectedIndex == 1
            ? NamingSource.CurrentDisplayName
            : NamingSource.ActualFilename;
    }

    private void OnPresetSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (PresetList.SelectedItem is NamingPresetDto preset)
        {
            ViewModel.Naming.LoadPreset(preset);
            NamingSourceSelector.SelectedIndex =
                ViewModel.Naming.Source == NamingSource.CurrentDisplayName
                    ? 1
                    : 0;
            RuleList.SelectedItem = ViewModel.Naming.SelectedRule;
        }
    }

    private void OnPresetContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        _ = sender;
        if (args.ItemContainer is ListViewItem container
            && args.Item is NamingPresetDto preset)
        {
            container.IsEnabled = preset.CanExecute;
        }
    }

    private void OnAddRuleClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        string kind = (RuleKindSelector.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "prefix";
        INamingRule rule = kind switch
        {
            "suffix" => new SuffixNamingRule(string.Empty),
            "replace" => new ReplaceNamingRule(string.Empty, string.Empty, false),
            "remove_text" => new RemoveTextNamingRule(string.Empty, false),
            "remove_brackets" => new RemoveBracketContentNamingRule(
                NamingBracketPair.Round),
            "case" => new CaseNamingRule(NamingCaseMode.Upper),
            "sequence" => new SequenceNamingRule(
                1,
                1,
                1,
                NamingPlacement.Suffix,
                "-"),
            "metadata_template" => new MetadataTemplateNamingRule("{name}"),
            _ => new PrefixNamingRule(string.Empty)
        };
        NamingRuleRowViewModel row = ViewModel.Naming.AddRule(rule);
        RuleList.SelectedItem = row;
    }

    private void OnRuleSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Naming.SelectedRule =
            RuleList.SelectedItem as NamingRuleRowViewModel;
        SynchronizeRuleEditor();
    }

    private void OnMoveRuleUpClicked(object sender, RoutedEventArgs args)
    {
        _ = args;
        if (sender is Button { Tag: NamingRuleRowViewModel row })
        {
            ViewModel.Naming.MoveRuleUp(row);
            RuleList.SelectedItem = row;
        }
    }

    private void OnMoveRuleDownClicked(object sender, RoutedEventArgs args)
    {
        _ = args;
        if (sender is Button { Tag: NamingRuleRowViewModel row })
        {
            ViewModel.Naming.MoveRuleDown(row);
            RuleList.SelectedItem = row;
        }
    }

    private void OnDeleteRuleClicked(object sender, RoutedEventArgs args)
    {
        _ = args;
        if (sender is Button { Tag: NamingRuleRowViewModel row })
        {
            ViewModel.Naming.DeleteRule(row);
            RuleList.SelectedItem = ViewModel.Naming.SelectedRule;
        }
    }

    private void OnBracketPairChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!_synchronizingRuleEditor
            && ViewModel.Naming.SelectedRule is { IsBracketRule: true } row
            && BracketPairSelector.SelectedIndex >= 0)
        {
            row.BracketPairParameter =
                (NamingBracketPair)BracketPairSelector.SelectedIndex;
        }
    }

    private void OnCaseModeChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!_synchronizingRuleEditor
            && ViewModel.Naming.SelectedRule is { IsCaseRule: true } row
            && CaseModeSelector.SelectedIndex >= 0)
        {
            row.CaseModeParameter = (NamingCaseMode)CaseModeSelector.SelectedIndex;
        }
    }

    private void OnSequencePlacementChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!_synchronizingRuleEditor
            && ViewModel.Naming.SelectedRule is { IsSequenceRule: true } row
            && SequencePlacementSelector.SelectedIndex >= 0)
        {
            row.SequencePlacementParameter =
                (NamingPlacement)SequencePlacementSelector.SelectedIndex;
        }
    }

    private void SynchronizeRuleEditor()
    {
        NamingRuleRowViewModel? row = ViewModel.Naming.SelectedRule;
        RuleEditor.Visibility = row is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        TextRuleEditor.Visibility = row?.IsTextRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReplaceRuleEditor.Visibility = row?.IsReplaceRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        BracketRuleEditor.Visibility = row?.IsBracketRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        CaseRuleEditor.Visibility = row?.IsCaseRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        SequenceRuleEditor.Visibility = row?.IsSequenceRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        TemplateRuleEditor.Visibility = row?.IsTemplateRule == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (row is null)
        {
            return;
        }

        _synchronizingRuleEditor = true;
        try
        {
            BracketPairSelector.SelectedIndex = (int)row.BracketPairParameter;
            CaseModeSelector.SelectedIndex = (int)row.CaseModeParameter;
            SequencePlacementSelector.SelectedIndex =
                (int)row.SequencePlacementParameter;
        }
        finally
        {
            _synchronizingRuleEditor = false;
        }
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!ViewModel.CanApply || ViewModel.IsSubmitting)
        {
            return;
        }

        await ViewModel.ApplyDraftAsync(_operationToken);
        if (ViewModel.LastResult is { Succeeded: true } result)
        {
            Result = new(true, result);
            Hide();
        }
        else if (ViewModel.HasFocusedProblem)
        {
            if (ViewModel.Naming.FirstErrorCode is not null
                && ViewModel.Naming.PreviewRows.Count > 0)
            {
                PreviewList.SelectedIndex = 0;
                PreviewList.Focus(FocusState.Programmatic);
            }
            else
            {
                DialogStatus.Focus(FocusState.Programmatic);
            }
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Hide();
    }

    private void OnClosing(
        ContentDialog sender,
        ContentDialogClosingEventArgs args)
    {
        _ = sender;
        if (ViewModel.IsSubmitting && !_operationToken.IsCancellationRequested)
        {
            args.Cancel = true;
        }
    }
}
