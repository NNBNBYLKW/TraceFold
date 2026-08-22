using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class TaxonomyPage : Page
{
    private readonly ILogger<TaxonomyPage> _logger;
    private CancellationTokenSource? _pageLifetime;

    public TaxonomyPage(
        TaxonomyViewModel viewModel,
        ILogger<TaxonomyPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TaxonomyViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        _pageLifetime?.Dispose();
        _pageLifetime = new CancellationTokenSource();
        await RunGuardedAsync(
            token => ViewModel.LoadAsync(token),
            "Load");
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        _pageLifetime?.Cancel();
    }

    private async void OnCreateClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            async token =>
            {
                string? name = await PromptForNameAsync(
                    "新建词条",
                    string.Empty);
                token.ThrowIfCancellationRequested();
                if (name is not null)
                {
                    await ViewModel.CreateAsync(name, token);
                }
            },
            "Create");
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (ViewModel.SelectedTerm is null)
        {
            return;
        }

        await RunGuardedAsync(
            async token =>
            {
                string? name = await PromptForNameAsync(
                    "重命名词条",
                    ViewModel.SelectedTerm.DisplayName);
                token.ThrowIfCancellationRequested();
                if (name is not null)
                {
                    await ViewModel.RenameAsync(name, token);
                }
            },
            "Rename");
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!ViewModel.DeleteCommand.CanExecute(null)
            || ViewModel.SelectedTerm is null)
        {
            return;
        }

        await RunGuardedAsync(
            async token =>
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "删除词条",
                    Content = $"确定删除“{ViewModel.SelectedTerm.DisplayName}”吗？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close
                };
                ContentDialogResult result = await dialog.ShowAsync();
                token.ThrowIfCancellationRequested();
                if (result == ContentDialogResult.Primary)
                {
                    await ViewModel.DeleteAsync(token);
                }
            },
            "Delete");
    }

    private async Task<string?> PromptForNameAsync(
        string title,
        string initialValue)
    {
        var input = new TextBox
        {
            Text = initialValue,
            PlaceholderText = "词条名称",
            MinWidth = 320
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = input,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? input.Text
            : null;
    }

    private async Task RunGuardedAsync(
        Func<CancellationToken, Task> operation,
        string operationName)
    {
        CancellationToken token = _pageLifetime?.Token ?? CancellationToken.None;
        try
        {
            await operation(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _logger.LogError(
                error,
                "TaxonomyPageOperationFailed Operation={Operation}",
                operationName);
            ViewModel.ReportOperationFailure();
        }
    }
}
