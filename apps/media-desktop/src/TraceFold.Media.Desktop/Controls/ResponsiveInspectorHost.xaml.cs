using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TraceFold.Media.Desktop.Controls;

/// <summary>
/// Presentation-only responsive host: one primary presenter and one inspector presenter.
/// The same inspector surface is repositioned between the wide column and the compact
/// drawer, so switching modes never duplicates content, and the host owns no selection,
/// navigation, metadata or command logic. Page code calls OpenDrawer/CloseDrawer and owns
/// focus restoration.
/// </summary>
public sealed partial class ResponsiveInspectorHost : UserControl
{
    private const double FallbackInspectorWidthDip = 360d;
    private bool applying;

    public ResponsiveInspectorHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public event EventHandler? LayoutModeChanged;

    public event EventHandler? DrawerOpened;

    public event EventHandler? DrawerClosed;

    public static readonly DependencyProperty PrimaryContentProperty =
        DependencyProperty.Register(
            nameof(PrimaryContent),
            typeof(object),
            typeof(ResponsiveInspectorHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty InspectorContentProperty =
        DependencyProperty.Register(
            nameof(InspectorContent),
            typeof(object),
            typeof(ResponsiveInspectorHost),
            new PropertyMetadata(null, OnInspectorContentChanged));

    public static readonly DependencyProperty HasInspectorContentProperty =
        DependencyProperty.Register(
            nameof(HasInspectorContent),
            typeof(bool),
            typeof(ResponsiveInspectorHost),
            new PropertyMetadata(false, OnLayoutAffectingPropertyChanged));

    public static readonly DependencyProperty IsDrawerOpenProperty =
        DependencyProperty.Register(
            nameof(IsDrawerOpen),
            typeof(bool),
            typeof(ResponsiveInspectorHost),
            new PropertyMetadata(false, OnLayoutAffectingPropertyChanged));

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(
            nameof(LayoutMode),
            typeof(WorkspaceLayoutMode),
            typeof(ResponsiveInspectorHost),
            new PropertyMetadata(WorkspaceLayoutMode.Compact));

    public object? PrimaryContent
    {
        get => GetValue(PrimaryContentProperty);
        set => SetValue(PrimaryContentProperty, value);
    }

    public object? InspectorContent
    {
        get => GetValue(InspectorContentProperty);
        set => SetValue(InspectorContentProperty, value);
    }

    public bool HasInspectorContent
    {
        get => (bool)GetValue(HasInspectorContentProperty);
        set => SetValue(HasInspectorContentProperty, value);
    }

    public bool IsDrawerOpen
    {
        get => (bool)GetValue(IsDrawerOpenProperty);
        set => SetValue(IsDrawerOpenProperty, value);
    }

    public WorkspaceLayoutMode LayoutMode
    {
        get => (WorkspaceLayoutMode)GetValue(LayoutModeProperty);
        private set => SetValue(LayoutModeProperty, value);
    }

    public void OpenDrawer()
    {
        if (LayoutMode != WorkspaceLayoutMode.Compact || !HasInspectorContent)
        {
            return;
        }

        IsDrawerOpen = true;
        ApplyLayout();
        DrawerOpened?.Invoke(this, EventArgs.Empty);
    }

    public void CloseDrawer()
    {
        if (!IsDrawerOpen)
        {
            return;
        }

        IsDrawerOpen = false;
        ApplyLayout();
        DrawerClosed?.Invoke(this, EventArgs.Empty);
    }

    private static double InspectorWidthDip =>
        Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(
            "TraceFoldInspectorWidth",
            out object? value) == true && value is double dip
            ? dip
            : FallbackInspectorWidthDip;

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        UpdateLayoutForWidth(ActualWidth);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateLayoutForWidth(e.NewSize.Width);

    private static void OnInspectorContentChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        var host = (ResponsiveInspectorHost)sender;
        host.HasInspectorContent = args.NewValue is not null;
        host.ApplyLayout();
    }

    private static void OnLayoutAffectingPropertyChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((ResponsiveInspectorHost)sender).ApplyLayout();

    private void UpdateLayoutForWidth(double widthDip)
    {
        WorkspaceLayoutMode mode = WorkspaceLayoutPolicy.GetMode(widthDip);
        bool changed = mode != LayoutMode;
        LayoutMode = mode;
        ApplyLayout();
        if (changed)
        {
            LayoutModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyLayout()
    {
        if (applying)
        {
            return;
        }

        applying = true;
        try
        {
            if (LayoutMode == WorkspaceLayoutMode.Wide)
            {
                // The wide workspace shows the inspector as a real pane and the drawer
                // chrome is not used at all.
                IsDrawerOpen = false;
                InspectorColumn.Width = new GridLength(InspectorWidthDip);
                Grid.SetColumn(PrimaryPresenter, 0);
                Grid.SetColumnSpan(PrimaryPresenter, 1);
                Grid.SetColumn(InspectorSurface, 1);
                Grid.SetColumnSpan(InspectorSurface, 1);
                InspectorSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
                InspectorSurface.Width = double.NaN;
                InspectorSurface.Visibility = HasInspectorContent
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                return;
            }

            InspectorColumn.Width = new GridLength(0);
            Grid.SetColumnSpan(PrimaryPresenter, 2);
            Grid.SetColumn(InspectorSurface, 0);
            Grid.SetColumnSpan(InspectorSurface, 2);
            InspectorSurface.HorizontalAlignment = HorizontalAlignment.Right;
            InspectorSurface.Width = InspectorWidthDip;
            InspectorSurface.Visibility = IsDrawerOpen && HasInspectorContent
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        finally
        {
            applying = false;
        }
    }
}
