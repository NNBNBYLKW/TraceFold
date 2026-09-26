using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TraceFold.Media.Application.Library;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    private bool _synchronizingPickers;
    private bool _synchronizingGridSelection;
    private bool _synchronizingSortControls;
    private bool _restoreListFocusAfterRefresh;

    public LibraryPage(
        LibraryViewModel viewModel,
        ITaxonomyService taxonomyService)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AuthorPicker.ViewModel = new TaxonomyPickerViewModel(taxonomyService)
        {
            Kind = TaxonomyKind.Author
        };
        SeriesPicker.ViewModel = new TaxonomyPickerViewModel(taxonomyService)
        {
            Kind = TaxonomyKind.Series
        };
        TagPicker.ViewModel = new TaxonomyPickerViewModel(taxonomyService)
        {
            Kind = TaxonomyKind.Tag
        };
        ViewModel.GridItemsReplacing += OnGridItemsReplacing;
        ViewModel.GridItemsReplaced += OnGridItemsReplaced;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public LibraryViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await ViewModel.LoadAsync(CancellationToken.None);
        SynchronizeSortControls();
        UpdateInspectorButtonVisibility();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        PreviewControl.Stop();
        await ViewModel.Inspector.Metadata.FlushAsync(CancellationToken.None);
    }

    private async void OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (_synchronizingGridSelection)
        {
            return;
        }

        ViewModel.ReplaceSelectionFromGrid(
            LibraryGrid.SelectedItems.Cast<LibraryCardViewModel>());
        if (ViewModel.Selection.IsSingleSelection)
        {
            await ViewModel.SelectAsync(
                ViewModel.Selection.SingleSelectedItem,
                CancellationToken.None);
            SynchronizePickers();
        }
        else
        {
            await ViewModel.SelectAsync(null, CancellationToken.None);
        }

        if (!ViewModel.Selection.HasSelection)
        {
            WorkspaceHost.CloseDrawer();
        }
    }

    private void OnGridItemsReplacing(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        _synchronizingGridSelection = true;
        _restoreListFocusAfterRefresh = IsFocusWithinLibraryList();
    }

    private void OnGridItemsReplaced(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        try
        {
            HashSet<Guid> selectedIds = ViewModel.Selection.SelectedIds.ToHashSet();
            LibraryGrid.SelectedItems.Clear();
            foreach (LibraryCardViewModel item in ViewModel.Items)
            {
                if (selectedIds.Contains(item.Id))
                {
                    LibraryGrid.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _synchronizingGridSelection = false;
        }

        if (_restoreListFocusAfterRefresh)
        {
            _restoreListFocusAfterRefresh = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (IsLoaded)
                {
                    FocusSelectedLibraryItem();
                }
            });
        }
    }

    private void OnSelectAllAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        LibraryGrid.SelectAll();
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
            LibraryGrid.SelectedItems.Clear();
        }

        args.Handled = true;
    }

    private void OnLibraryContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        _ = sender;
        if (args.Item is LibraryCardViewModel card && args.ItemContainer is not null)
        {
            AutomationProperties.SetName(args.ItemContainer, card.AccessibleName);
        }
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
                LibraryInspectorHeading.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnInspectorDrawerClosed(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (IsLoaded)
        {
            FocusSelectedLibraryItem();
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
    /// reload, refilter or flush anything.
    /// </summary>
    private void UpdateInspectorButtonVisibility()
    {
        Visibility visibility = WorkspaceHost.LayoutMode == WorkspaceLayoutMode.Compact
            ? Visibility.Visible
            : Visibility.Collapsed;
        LibraryInspectorOpenButton.Visibility = visibility;
        LibraryInspectorCloseButton.Visibility = visibility;
    }

    private void FocusSelectedLibraryItem()
    {
        LibraryCardViewModel? item = ViewModel.Selection.SingleSelectedItem;
        if (item is null && ViewModel.Selection.HasSelection)
        {
            Guid firstId = ViewModel.Selection.SelectedIds[0];
            item = ViewModel.Items.FirstOrDefault(card => card.Id == firstId);
        }

        if (item is null)
        {
            LibraryGrid.Focus(FocusState.Programmatic);
            return;
        }

        FocusLibraryItem(item);
    }

    private void FocusLibraryItem(LibraryCardViewModel item)
    {
        LibraryGrid.ScrollIntoView(item);
        LibraryGrid.UpdateLayout();
        if (LibraryGrid.ContainerFromItem(item) is ListViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            // A virtualized row may be realized only after the next layout pass.
            LibraryGrid.Focus(FocusState.Programmatic);
        }
    }

    private bool IsFocusWithinLibraryList()
    {
        if (XamlRoot is null)
        {
            return false;
        }

        DependencyObject? focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, LibraryGrid))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        LibraryGrid.SelectedItems.Clear();
    }

    private async void OnBatchEditClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await ViewModel.BatchEditCommand.ExecuteAsync(null);
        LibraryGrid.Focus(FocusState.Programmatic);
    }

    private async void OnUndoBatchClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        await ViewModel.UndoBatchCommand.ExecuteAsync(null);
        LibraryGrid.Focus(FocusState.Programmatic);
    }

    private async void OnSortSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (_synchronizingSortControls
            || !TryReadSelection(SortFieldSelector, out LibrarySortField field)
            || !TryReadSelection(
                SortDirectionSelector,
                out LibrarySortDirection direction))
        {
            return;
        }

        await ViewModel.ChangeSortAsync(field, direction, CancellationToken.None);
        SynchronizeSortControls();
    }

    private void OnFilterButtonClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Filter.BeginEdit();
        SelectTaggedItem(MediaFilterSelector, ViewModel.Filter.Media.ToString());
        SelectTaggedItem(
            DisplayNameFilterSelector,
            ViewModel.Filter.DisplayName.ToString());
    }

    private async void OnFilterApplyClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        if (!TryReadSelection(MediaFilterSelector, out LibraryMediaFilter media)
            || !TryReadSelection(
                DisplayNameFilterSelector,
                out LibraryDisplayNameFilter displayName))
        {
            return;
        }

        ViewModel.Filter.Media = media;
        ViewModel.Filter.DisplayName = displayName;
        ViewModel.Filter.ApplyDraft();
        FilterFlyout.Hide();
        await ViewModel.ApplyCurrentFilterAsync(CancellationToken.None);
    }

    private void OnFilterCancelClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Filter.CancelDraft();
        FilterFlyout.Hide();
    }

    private async void OnAuthorFilterTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            await ViewModel.Filter.Authors.SearchAsync(
                sender.Text,
                CancellationToken.None);
        }
    }

    private async void OnSeriesFilterTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            await ViewModel.Filter.Series.SearchAsync(
                sender.Text,
                CancellationToken.None);
        }
    }

    private async void OnTagFilterTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            await ViewModel.Filter.Tags.SearchAsync(
                sender.Text,
                CancellationToken.None);
        }
    }

    private void OnAuthorFilterSuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args) =>
        SelectSuggestion(sender, args, ViewModel.Filter.Authors);

    private void OnSeriesFilterSuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args) =>
        SelectSuggestion(sender, args, ViewModel.Filter.Series);

    private void OnTagFilterSuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args) =>
        SelectSuggestion(sender, args, ViewModel.Filter.Tags);

    private void OnRemoveAuthorFilterTerm(object sender, RoutedEventArgs args) =>
        RemoveFilterTerm(sender, ViewModel.Filter.Authors);

    private void OnRemoveSeriesFilterTerm(object sender, RoutedEventArgs args) =>
        RemoveFilterTerm(sender, ViewModel.Filter.Series);

    private void OnRemoveTagFilterTerm(object sender, RoutedEventArgs args) =>
        RemoveFilterTerm(sender, ViewModel.Filter.Tags);

    private async void OnRemoveActiveFilterClick(
        object sender,
        RoutedEventArgs args)
    {
        _ = args;
        if (sender is FrameworkElement
            {
                DataContext: LibraryFilterChipViewModel chip
            })
        {
            chip.Remove();
            await ViewModel.ApplyCurrentFilterAsync(CancellationToken.None);
        }
    }

    private async void OnClearFiltersClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        ViewModel.Filter.ClearAll();
        await ViewModel.ApplyCurrentFilterAsync(CancellationToken.None);
    }

    private void OnAuthorSelectionChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        ApplyPicker(AuthorPicker);
    }

    private void OnSeriesSelectionChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        ApplyPicker(SeriesPicker);
    }

    private void OnTagSelectionChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        ApplyPicker(TagPicker);
    }

    private void ApplyPicker(TaxonomyPickerControl picker)
    {
        TaxonomyPickerViewModel? pickerViewModel = picker.ViewModel;
        if (_synchronizingPickers || pickerViewModel is null)
        {
            return;
        }

        ViewModel.Inspector.Metadata.ApplyTaxonomySelection(
            pickerViewModel.Kind,
            pickerViewModel.SelectedTerms);
    }

    private void SynchronizePickers()
    {
        _synchronizingPickers = true;
        try
        {
            MetadataEditorViewModel metadata = ViewModel.Inspector.Metadata;
            AuthorPicker.ViewModel!.SetSelectedTerms(metadata.Authors);
            SeriesPicker.ViewModel!.SetSelectedTerms(
                metadata.Series is null ? [] : [metadata.Series]);
            TagPicker.ViewModel!.SetSelectedTerms(metadata.Tags);
        }
        finally
        {
            _synchronizingPickers = false;
        }
    }

    private void SynchronizeSortControls()
    {
        _synchronizingSortControls = true;
        try
        {
            SelectTaggedItem(
                SortFieldSelector,
                ViewModel.Filter.AppliedQuery.SortField.ToString());
            SelectTaggedItem(
                SortDirectionSelector,
                ViewModel.Filter.AppliedQuery.SortDirection.ToString());
        }
        finally
        {
            _synchronizingSortControls = false;
        }
    }

    private static void SelectSuggestion(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args,
        ExistingTaxonomyFilterViewModel filter)
    {
        if (args.SelectedItem is TaxonomyTermDto term)
        {
            filter.Select(term);
            sender.Text = string.Empty;
        }
    }

    private static void RemoveFilterTerm(
        object sender,
        ExistingTaxonomyFilterViewModel filter)
    {
        if (sender is FrameworkElement { DataContext: TaxonomyTermDto term })
        {
            filter.Remove(term);
        }
    }

    private static bool TryReadSelection<TEnum>(
        ComboBox selector,
        out TEnum value)
        where TEnum : struct, Enum
    {
        if (selector.SelectedItem is ComboBoxItem { Tag: string raw }
            && Enum.TryParse(raw, ignoreCase: false, out value)
            && Enum.IsDefined(value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static void SelectTaggedItem(ComboBox selector, string value)
    {
        selector.SelectedItem = selector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag, value));
    }
}
