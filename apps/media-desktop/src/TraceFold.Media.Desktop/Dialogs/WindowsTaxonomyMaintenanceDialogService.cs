using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Dialogs;

public sealed class WindowsTaxonomyMaintenanceDialogService(
    ITaxonomyManagementQueryService query,
    ITaxonomyMaintenanceService maintenance,
    WindowContext windowContext) : ITaxonomyMaintenanceDialogService
{
    public async Task<TaxonomyMergeDialogResult?> ShowMergeAsync(
        IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
        Guid? preselectedTargetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedTerms);
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = new TaxonomyMergeDialogViewModel(
            selectedTerms,
            preselectedTargetId,
            query,
            maintenance);
        var dialog = new TaxonomyMergeDialog(viewModel)
        {
            XamlRoot = ResolveXamlRoot()
        };
        return await dialog.ShowMergeAsync(cancellationToken);
    }

    public async Task<bool> ConfirmDeleteAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!preparation.CanDelete)
        {
            return false;
        }

        var terms = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 280,
            IsItemClickEnabled = false
        };
        AutomationProperties.SetAutomationId(terms, "TaxonomyDeleteTermList");
        AutomationProperties.SetName(terms, "将删除的未使用词条列表");
        foreach (TaxonomyTermSnapshot term in preparation.Terms)
        {
            terms.Items.Add(new ListViewItem
            {
                Content = term.DisplayName,
                IsTabStop = false
            });
        }

        var content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 420
        };
        content.Children.Add(new TextBlock
        {
            Text = "将从软件词表中删除以下未使用词条。实际文件不会被修改。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(terms);
        var dialog = new ContentDialog
        {
            XamlRoot = ResolveXamlRoot(),
            Title = $"删除 {preparation.Terms.Count} 个未使用词条",
            Content = content,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        AutomationProperties.SetAutomationId(dialog, "TaxonomyDeleteConfirmDialog");
        AutomationProperties.SetName(dialog, "确认删除未使用词条");
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() => dialog.DispatcherQueue.TryEnqueue(
                dialog.Hide));
        ContentDialogResult result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary;
    }

    private XamlRoot ResolveXamlRoot()
    {
        MainWindow window = windowContext.MainWindow
            ?? throw new InvalidOperationException("Main window is not available.");
        FrameworkElement content = window.Content as FrameworkElement
            ?? throw new InvalidOperationException(
                "Main window content is not available.");
        return content.XamlRoot
            ?? throw new InvalidOperationException(
                "Main window XAML root is not available.");
    }
}
