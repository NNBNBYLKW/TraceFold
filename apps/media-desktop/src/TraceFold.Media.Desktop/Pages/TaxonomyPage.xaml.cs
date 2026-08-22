using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class TaxonomyPage : Page
{
    public TaxonomyPage(TaxonomyViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public TaxonomyViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await ViewModel.LoadAsync(CancellationToken.None);
    }

    private async void OnCreateClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        string? name = await PromptForNameAsync("新建词条", string.Empty);
        if (name is not null)
        {
            await ViewModel.CreateAsync(name, CancellationToken.None);
        }
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (ViewModel.SelectedTerm is null)
        {
            return;
        }

        string? name = await PromptForNameAsync(
            "重命名词条",
            ViewModel.SelectedTerm.DisplayName);
        if (name is not null)
        {
            await ViewModel.RenameAsync(name, CancellationToken.None);
        }
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除词条",
            Content = $"确定删除“{ViewModel.SelectedTerm.DisplayName}”吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteAsync(CancellationToken.None);
        }
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
}
