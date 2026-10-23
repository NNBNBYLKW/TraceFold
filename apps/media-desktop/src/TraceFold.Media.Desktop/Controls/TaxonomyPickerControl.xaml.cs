using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceFold.Media.Application.Metadata;

namespace TraceFold.Media.Desktop.Controls;

public sealed partial class TaxonomyPickerControl : UserControl
{
    private TaxonomyPickerViewModel? _viewModel;

    public TaxonomyPickerControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty InputAutomationIdProperty =
        DependencyProperty.Register(
            nameof(InputAutomationId),
            typeof(string),
            typeof(TaxonomyPickerControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InputAutomationNameProperty =
        DependencyProperty.Register(
            nameof(InputAutomationName),
            typeof(string),
            typeof(TaxonomyPickerControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedTermsAutomationIdProperty =
        DependencyProperty.Register(
            nameof(SelectedTermsAutomationId),
            typeof(string),
            typeof(TaxonomyPickerControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SelectedTermsAutomationNameProperty =
        DependencyProperty.Register(
            nameof(SelectedTermsAutomationName),
            typeof(string),
            typeof(TaxonomyPickerControl),
            new PropertyMetadata(string.Empty));

    public string InputAutomationId
    {
        get => (string)GetValue(InputAutomationIdProperty);
        set => SetValue(InputAutomationIdProperty, value);
    }

    public string InputAutomationName
    {
        get => (string)GetValue(InputAutomationNameProperty);
        set => SetValue(InputAutomationNameProperty, value);
    }

    public string SelectedTermsAutomationId
    {
        get => (string)GetValue(SelectedTermsAutomationIdProperty);
        set => SetValue(SelectedTermsAutomationIdProperty, value);
    }

    public string SelectedTermsAutomationName
    {
        get => (string)GetValue(SelectedTermsAutomationNameProperty);
        set => SetValue(SelectedTermsAutomationNameProperty, value);
    }

    public TaxonomyPickerViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            if (_viewModel is not null)
            {
                _viewModel.SelectionChanged -= OnSelectionChanged;
            }

            _viewModel = value;
            if (_viewModel is not null)
            {
                _viewModel.SelectionChanged += OnSelectionChanged;
            }

            Bindings.Update();
        }
    }

    public event EventHandler? SelectionChanged;

    private async void OnTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput
            || _viewModel is null)
        {
            return;
        }

        await _viewModel.SearchAsync(sender.Text, CancellationToken.None);
    }

    private async void OnQuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string value = args.ChosenSuggestion is TaxonomyTermDto selected
            ? selected.DisplayName
            : args.QueryText;
        if (string.IsNullOrWhiteSpace(value) || _viewModel is null)
        {
            return;
        }

        await _viewModel.CommitAsync(value, CancellationToken.None);
        sender.Text = string.Empty;
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs args)
    {
        _ = args;
        if (sender is Button { Tag: TaxonomyTermDto term } && _viewModel is not null)
        {
            _viewModel.Remove(term);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        _ = sender;
        SelectionChanged?.Invoke(this, args);
    }
}
