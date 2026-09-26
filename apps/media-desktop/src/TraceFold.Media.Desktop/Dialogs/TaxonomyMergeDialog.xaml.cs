using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class TaxonomyMergeDialog : ContentDialog
{
    private CancellationToken _operationToken;
    private bool _synchronizingTarget;

    public TaxonomyMergeDialog(TaxonomyMergeDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        SizeChanged += OnViewportSizeChanged;
    }

    public TaxonomyMergeDialogViewModel ViewModel { get; }

    public TaxonomyMergeDialogResult? Result { get; private set; }

    public async Task<TaxonomyMergeDialogResult?> ShowMergeAsync(
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
        UpdateViewportBounds();
        await RunGuardedAsync(async () =>
        {
            await ViewModel.InitializeAsync(_operationToken);
            SynchronizeSelectedTarget();
            if (ViewModel.HasPreselectedTarget)
            {
                AffectedFilesList.StartBringIntoView();
                AffectedFilesList.Focus(FocusState.Programmatic);
            }
            else
            {
                TargetSearchBox.StartBringIntoView();
                TargetSearchBox.Focus(FocusState.Programmatic);
            }
        });
    }

    private async void OnTargetSearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        await RunGuardedAsync(async () =>
        {
            await ViewModel.SearchTargetsAsync(sender.Text, _operationToken);
            SynchronizeSelectedTarget();
        });
    }

    private async void OnTargetSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (_synchronizingTarget)
        {
            return;
        }

        await RunGuardedAsync(() => ViewModel.SelectTargetAsync(
            TargetResultsList.SelectedItem as TaxonomyManagementTermDto,
            _operationToken));
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(async () =>
        {
            TaxonomyMaintenanceResult? result =
                await ViewModel.ApplyAsync(_operationToken);
            if (result?.Succeeded == true)
            {
                Result = new TaxonomyMergeDialogResult(
                    ViewModel.Target!.Id,
                    result);
                Hide();
                return;
            }

            if (ViewModel.Preview.ErrorCode is
                TaxonomyMaintenanceErrorCodes.NameInvalid
                or TaxonomyMaintenanceErrorCodes.NameConflict)
            {
                FinalNameInput.StartBringIntoView();
                FinalNameInput.Focus(FocusState.Programmatic);
            }
            else
            {
                TargetResultsList.StartBringIntoView();
                TargetResultsList.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnCancelClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Result = null;
        Hide();
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

    private void SynchronizeSelectedTarget()
    {
        _synchronizingTarget = true;
        try
        {
            TargetResultsList.SelectedItem = ViewModel.Target is null
                ? null
                : ViewModel.TargetResults.FirstOrDefault(term =>
                    term.Id == ViewModel.Target.Id);
        }
        finally
        {
            _synchronizingTarget = false;
        }
    }

    private async Task RunGuardedAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) when (_operationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ViewModel.ReportOperationFailure();
        }
    }
}
