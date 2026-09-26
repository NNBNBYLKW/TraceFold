using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Desktop.Controls;
using Microsoft.UI.Xaml.Input;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class TaxonomyPage : Page
{
    private readonly ILogger<TaxonomyPage> _logger;
    private CancellationTokenSource? _pageLifetime;
    private bool _synchronizingTermSelection;

    public TaxonomyPage(
        TaxonomyViewModel viewModel,
        ILogger<TaxonomyPage> logger)
    {
        ViewModel = viewModel;
        _logger = logger;
        InitializeComponent();
        ViewModel.TermsReplacing += OnTermsReplacing;
        ViewModel.TermsReplaced += OnTermsReplaced;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TaxonomyViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateInspectorButtonVisibility();
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

    private void OnKindSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (KindList.SelectedItem is TaxonomyKindOption option)
        {
            ViewModel.SelectedKind = option.Kind;
        }
    }

    private void OnSearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
        }
    }

    private void OnReferenceFilterChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (TryReadTag(
                ReferenceFilterSelector,
                out TaxonomyReferenceFilter filter))
        {
            ViewModel.ReferenceFilter = filter;
        }
    }

    private void OnSortChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (TryReadTag(SortFieldSelector, out TaxonomySortField field)
            && TryReadTag(
                SortDirectionSelector,
                out TaxonomySortDirection direction))
        {
            ViewModel.SortField = field;
            ViewModel.SortDirection = direction;
        }
    }

    private void OnTermSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (_synchronizingTermSelection)
        {
            return;
        }

        ViewModel.ReplaceSelection(
            TermList.SelectedItems.Cast<TaxonomyManagementTermDto>());
        if (!ViewModel.Selection.HasSelection)
        {
            WorkspaceHost.CloseDrawer();
        }
    }

    private void OnTermsReplacing(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        _synchronizingTermSelection = true;
    }

    private void OnTermsReplaced(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        try
        {
            HashSet<Guid> selectedIds = ViewModel.Selection.SelectedIds.ToHashSet();
            TermList.SelectedItems.Clear();
            foreach (TaxonomyManagementTermDto term in ViewModel.Terms)
            {
                if (selectedIds.Contains(term.Id))
                {
                    TermList.SelectedItems.Add(term);
                }
            }
        }
        finally
        {
            _synchronizingTermSelection = false;
        }
    }

    private void OnSelectAllAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        TermList.SelectAll();
        args.Handled = true;
    }

    private void OnClearSelectionAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        if (WorkspaceHost.IsDrawerOpen)
        {
            WorkspaceHost.CloseDrawer();
        }
        else
        {
            TermList.SelectedItems.Clear();
        }

        args.Handled = true;
    }

    private void OnInspectorOpenClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        WorkspaceHost.OpenDrawer();
    }

    private void OnInspectorCloseClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        WorkspaceHost.CloseDrawer();
    }

    private void OnInspectorDrawerOpened(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded && WorkspaceHost.IsDrawerOpen)
            {
                WorkspaceHost.UpdateLayout();
                TaxonomyInspectorHeading.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnInspectorDrawerClosed(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (IsLoaded)
        {
            FocusSelectedTaxonomyItem();
        }
    }

    private void OnWorkspaceLayoutModeChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateInspectorButtonVisibility();
    }

    /// <summary>
    /// Layout-mode changes only show or hide the compact drawer affordances; they never
    /// reload, refilter, run diagnostics or write anything.
    /// </summary>
    private void UpdateInspectorButtonVisibility()
    {
        Visibility visibility = WorkspaceHost.LayoutMode == WorkspaceLayoutMode.Compact
            ? Visibility.Visible
            : Visibility.Collapsed;
        TaxonomyInspectorOpenButton.Visibility = visibility;
        TaxonomyInspectorCloseButton.Visibility = visibility;
    }

    private void FocusSelectedTaxonomyItem()
    {
        TaxonomyManagementTermDto? term = ViewModel.Selection.SingleSelectedTerm;
        if (term is null && ViewModel.Selection.HasSelection)
        {
            Guid firstId = ViewModel.Selection.SelectedIds[0];
            term = TermList.Items.OfType<TaxonomyManagementTermDto>()
                .FirstOrDefault(candidate => candidate.Id == firstId);
        }

        if (term is null)
        {
            TermList.Focus(FocusState.Programmatic);
            return;
        }

        TermList.ScrollIntoView(term);
        TermList.UpdateLayout();
        if (TermList.ContainerFromItem(term) is ListViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            // A virtualized row may be realized only after the next layout pass.
            TermList.Focus(FocusState.Programmatic);
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            token => ViewModel.LoadAsync(token),
            "Refresh");
    }

    private async void OnCreateClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            async token =>
            {
                string? name = await PromptForNameAsync(
                    $"新建{ViewModel.SelectedKindDisplayName}词条",
                    string.Empty);
                token.ThrowIfCancellationRequested();
                if (name is not null)
                {
                    await ViewModel.CreateAsync(name, token);
                    TermList.Focus(FocusState.Programmatic);
                }
            },
            "Create");
    }

    private async void OnRenameClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        TaxonomyManagementTermDto? selected = ViewModel.SelectedTerm;
        if (selected is null)
        {
            return;
        }

        await RunGuardedAsync(
            async token =>
            {
                string? name = await PromptForNameAsync(
                    "重命名词条",
                    selected.DisplayName);
                token.ThrowIfCancellationRequested();
                if (name is not null)
                {
                    await ViewModel.RenameAsync(name, token);
                    TermList.Focus(FocusState.Programmatic);
                }
            },
            "Rename");
    }

    private async void OnMergeClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            async token =>
            {
                await ViewModel.MergeAsync(token);
                TermList.Focus(FocusState.Programmatic);
            },
            "Merge");
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            async token =>
            {
                await ViewModel.DeleteSelectionAsync(token);
                TermList.Focus(FocusState.Programmatic);
            },
            "Delete");
    }

    private async void OnUndoClicked(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await RunGuardedAsync(
            async token =>
            {
                await ViewModel.UndoMaintenanceAsync(token);
                TermList.Focus(FocusState.Programmatic);
            },
            "Undo");
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

    private static bool TryReadTag<T>(ComboBox? selector, out T value)
        where T : struct, Enum
    {
        if (selector is null)
        {
            value = default;
            return false;
        }

        string? tag = (selector.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse(tag, ignoreCase: false, out value);
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
