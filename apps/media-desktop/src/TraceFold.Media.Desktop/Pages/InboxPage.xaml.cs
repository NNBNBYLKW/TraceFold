using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class InboxPage : Page
{
    private bool _synchronizingPickers;
    private bool _synchronizingGridSelection;
    private bool _restoreListFocusAfterRefresh;

    public InboxPage(
        InboxViewModel viewModel,
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
        ViewModel.FocusRequested += OnFocusRequested;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public InboxViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateInspectorButtonVisibility();
        await ViewModel.LoadAsync(CancellationToken.None);
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

        await ViewModel.SelectManySafeAsync(
            InboxGrid.SelectedItems.Cast<InboxCardViewModel>().ToArray(),
            CancellationToken.None);
        if (ViewModel.Selection.IsSingleSelection)
        {
            SynchronizePickers();
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
        _restoreListFocusAfterRefresh = IsFocusWithinInboxList();
    }

    private void OnGridItemsReplaced(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        try
        {
            HashSet<Guid> selectedIds = ViewModel.Selection.SelectedIds.ToHashSet();
            InboxGrid.SelectedItems.Clear();
            foreach (InboxCardViewModel item in ViewModel.Items)
            {
                if (selectedIds.Contains(item.Id))
                {
                    InboxGrid.SelectedItems.Add(item);
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
                    FocusSelectedInboxItem();
                }
            });
        }
    }

    private void OnSelectAllAccelerator(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        InboxGrid.SelectAll();
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
            InboxGrid.SelectedItems.Clear();
        }

        args.Handled = true;
    }

    private void OnInboxContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        _ = sender;
        if (args.Item is InboxCardViewModel card && args.ItemContainer is not null)
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
                InboxInspectorHeading.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnInspectorDrawerClosed(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        if (IsLoaded)
        {
            FocusSelectedInboxItem();
        }
    }

    private void OnWorkspaceLayoutModeChanged(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        UpdateInspectorButtonVisibility();
    }

    private void UpdateInspectorButtonVisibility()
    {
        Visibility visibility = WorkspaceHost.LayoutMode == WorkspaceLayoutMode.Compact
            ? Visibility.Visible
            : Visibility.Collapsed;
        InboxInspectorOpenButton.Visibility = visibility;
        InboxInspectorCloseButton.Visibility = visibility;
    }

    private void OnCancelSelectionClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        InboxGrid.SelectedItems.Clear();
    }

    private void OnFocusRequested(object? sender, InboxFocusRequest request)
    {
        _ = sender;
        DispatcherQueue.TryEnqueue(() => ApplyFocusRequest(request));
    }

    private void ApplyFocusRequest(InboxFocusRequest request)
    {
        switch (request.Target)
        {
            case InboxFocusTarget.List:
                InboxGrid.Focus(FocusState.Programmatic);
                break;
            case InboxFocusTarget.Item when request.InboxItemId.HasValue:
                InboxCardViewModel? item = ViewModel.Items.FirstOrDefault(
                    card => card.Id == request.InboxItemId.Value);
                if (item is null)
                {
                    InboxTitle.Focus(FocusState.Programmatic);
                    break;
                }

                FocusInboxItem(item);

                break;
            default:
                InboxTitle.Focus(FocusState.Programmatic);
                break;
        }
    }

    private void FocusSelectedInboxItem()
    {
        InboxCardViewModel? item = ViewModel.Selection.SingleSelectedItem;
        if (item is null && ViewModel.Selection.HasSelection)
        {
            Guid firstId = ViewModel.Selection.SelectedIds[0];
            item = ViewModel.Items.FirstOrDefault(card => card.Id == firstId);
        }

        if (item is null)
        {
            InboxGrid.Focus(FocusState.Programmatic);
            return;
        }

        FocusInboxItem(item);
    }

    private void FocusInboxItem(InboxCardViewModel item)
    {
        InboxGrid.ScrollIntoView(item);
        InboxGrid.UpdateLayout();
        if (InboxGrid.ContainerFromItem(item) is ListViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            // A virtualized row may be realized only after the next layout pass.
            InboxGrid.Focus(FocusState.Programmatic);
        }
    }

    private bool IsFocusWithinInboxList()
    {
        if (XamlRoot is null)
        {
            return false;
        }

        DependencyObject? focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, InboxGrid))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
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
}
