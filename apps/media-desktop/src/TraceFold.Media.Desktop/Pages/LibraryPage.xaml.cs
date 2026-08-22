using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Metadata;
using TraceFold.Media.Domain.Metadata;
using TraceFold.Media.Desktop.Controls;

namespace TraceFold.Media.Desktop.Pages;

public sealed partial class LibraryPage : Page
{
    private bool _synchronizingPickers;

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
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public LibraryViewModel ViewModel { get; }

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
        await ViewModel.SelectAsync(
            LibraryGrid.SelectedItem as LibraryCardViewModel,
            CancellationToken.None);
        SynchronizePickers();
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
