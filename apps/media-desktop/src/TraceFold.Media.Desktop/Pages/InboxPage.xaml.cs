using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class InboxPage : Page
{
    private bool _synchronizingPickers;
    private bool _synchronizingGridSelection;

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
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public InboxViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
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

        await ViewModel.SelectManyAsync(
            InboxGrid.SelectedItems.Cast<InboxCardViewModel>().ToArray(),
            CancellationToken.None);
        if (ViewModel.Selection.IsSingleSelection)
        {
            SynchronizePickers();
        }
    }

    private void OnGridItemsReplacing(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        _synchronizingGridSelection = true;
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
        InboxGrid.SelectedItems.Clear();
        args.Handled = true;
    }

    private void OnCancelSelectionClick(object sender, RoutedEventArgs args)
    {
        _ = sender;
        _ = args;
        InboxGrid.SelectedItems.Clear();
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
