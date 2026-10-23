using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Intake;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class BatchMetadataDialog : ContentDialog
{
    private readonly Func<BatchMetadataCommand, CancellationToken,
        Task<BatchMetadataResult>> _submit;
    private CancellationToken _operationToken;

    public BatchMetadataDialog(
        BatchMetadataDialogViewModel viewModel,
        BatchMetadataEditorDto editor,
        IReadOnlyList<InboxSelectionEntry> selection,
        Func<BatchMetadataCommand, CancellationToken,
            Task<BatchMetadataResult>> submit)
    {
        ViewModel = viewModel;
        _submit = submit;
        ViewModel.Load(editor, selection);
        InitializeComponent();
        SizeChanged += OnViewportSizeChanged;
    }

    public BatchMetadataDialogViewModel ViewModel { get; }

    public BatchMetadataDialogResult Result { get; private set; } =
        new(false, null);

    public async Task<BatchMetadataDialogResult> ShowEditorAsync(
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

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateViewportBounds();
        AddAuthorInput.Focus(FocusState.Programmatic);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateViewportBounds();
    }

    private void UpdateViewportBounds()
    {
        if (XamlRoot is null) return;
        double width = Math.Max(320, XamlRoot.Size.Width - 112);
        double height = Math.Max(240, XamlRoot.Size.Height - 160);
        if (DialogRoot.MaxWidth != width) DialogRoot.MaxWidth = width;
        if (DialogRoot.MaxHeight != height) DialogRoot.MaxHeight = height;
    }

    private void OnModeTabSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Mode = ModeTabs.SelectedIndex switch
        {
            1 => BatchMetadataMode.Remove,
            2 => BatchMetadataMode.Replace,
            _ => BatchMetadataMode.Add
        };
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

        PickerForCurrentMode(value.Kind)?.Remove(value);
    }

    private BatchTaxonomyPickerViewModel? PickerForCurrentMode(
        TaxonomyKind kind) => (ViewModel.Mode, kind) switch
    {
        (BatchMetadataMode.Add, TaxonomyKind.Author) =>
            ViewModel.AddAuthorPicker,
        (BatchMetadataMode.Add, TaxonomyKind.Tag) =>
            ViewModel.AddTagPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Author) =>
            ViewModel.ReplaceAuthorPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Series) =>
            ViewModel.ReplaceSeriesPicker,
        (BatchMetadataMode.Replace, TaxonomyKind.Tag) =>
            ViewModel.ReplaceTagPicker,
        _ => null
    };

    private async void OnSubmitClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (ViewModel.IsSubmitting
            || !ViewModel.TryBuildCommand(
                out BatchMetadataCommand? command,
                out _))
        {
            return;
        }

        ViewModel.IsSubmitting = true;
        try
        {
            BatchMetadataResult result = await _submit(
                command!,
                _operationToken);
            ViewModel.ApplyResult(result);
            if (result.Succeeded)
            {
                Result = new(true, result);
                Hide();
            }
        }
        catch (OperationCanceledException) when (_operationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ViewModel.ReportInputError(error.Message);
        }
        finally
        {
            ViewModel.IsSubmitting = false;
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
        if (ViewModel.IsSubmitting && !Result.Applied
            && !_operationToken.IsCancellationRequested)
        {
            args.Cancel = true;
        }
    }
}
