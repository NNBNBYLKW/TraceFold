using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Desktop.Controls;
using TraceFold.Media.Desktop.Pages;

namespace TraceFold.Media.Desktop.Shell;

public sealed partial class ShellPage : Page
{
    private readonly IReadOnlyDictionary<string, Page> _pages;

    public ShellPage(
        ShellViewModel viewModel,
        InboxPage inboxPage,
        LibraryPage libraryPage,
        TaxonomyPage taxonomyPage,
        SettingsPage settingsPage)
    {
        ViewModel = viewModel;
        _pages = new Dictionary<string, Page>(StringComparer.Ordinal)
        {
            ["inbox"] = inboxPage,
            ["library"] = libraryPage,
            ["taxonomy"] = taxonomyPage,
            ["settings"] = settingsPage
        };
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public ShellViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectAndShow(ViewModel.SelectedPageKey);
        ApplyNavigationMode(ActualWidth);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyNavigationMode(e.NewSize.Width);

    /// <summary>
    /// Keeps the navigation rail in step with the real window width. The decision comes from
    /// the shared policy and the result stays in the view: no layout state is written into
    /// the shell view model.
    /// </summary>
    private void ApplyNavigationMode(double widthDip)
    {
        bool wide = WorkspaceLayoutPolicy.GetMode(widthDip) == WorkspaceLayoutMode.Wide;
        Navigation.PaneDisplayMode = wide
            ? NavigationViewPaneDisplayMode.Left
            : NavigationViewPaneDisplayMode.LeftCompact;
        Navigation.IsPaneOpen = wide;
    }

    private void OnSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key)
        {
            return;
        }

        ViewModel.SelectedPageKey = key;
        ShowPage(key);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ShellViewModel.SelectedPageKey))
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            SelectAndShow(ViewModel.SelectedPageKey);
        }
        else
        {
            DispatcherQueue.TryEnqueue(
                () => SelectAndShow(ViewModel.SelectedPageKey));
        }
    }

    private void SelectAndShow(string key)
    {
        NavigationViewItem? item = Navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => candidate.Tag as string == key);
        if (item is not null && !ReferenceEquals(Navigation.SelectedItem, item))
        {
            Navigation.SelectedItem = item;
        }

        ShowPage(key);
    }

    private void ShowPage(string key)
    {
        if (_pages.TryGetValue(key, out Page? page)
            && !ReferenceEquals(ContentFrame.Content, page))
        {
            ContentFrame.Content = page;
        }
    }
}
