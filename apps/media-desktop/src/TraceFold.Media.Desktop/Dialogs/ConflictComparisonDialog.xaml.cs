using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Domain.Ingest;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed partial class ConflictComparisonDialog : ContentDialog
{
    public ConflictComparisonDialog(ConflictCaseDto conflict)
    {
        ViewModel = new ConflictDialogViewModel(conflict);
        InitializeComponent();
    }

    public ConflictDialogViewModel ViewModel { get; }

    public ConflictDialogResult? Decision { get; private set; }

    public async Task<ConflictDialogResult?> ShowDecisionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() =>
                DispatcherQueue.TryEnqueue(Hide));
        await ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return Decision;
    }

    private void OnSkipClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Decide(ConflictResolution.Skip, null);
    }

    private void OnKeepBothClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        Decide(ConflictResolution.KeepBoth, null);
    }

    private void OnReplaceClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (ViewModel.SelectedMatch is not null && ViewModel.CanReplace)
        {
            Decide(ConflictResolution.Replace, ViewModel.SelectedMatch.Id);
        }
    }

    private void Decide(ConflictResolution resolution, Guid? selectedMatchId)
    {
        Decision = new ConflictDialogResult(resolution, selectedMatchId);
        Hide();
    }
}
