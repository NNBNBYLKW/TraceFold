using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

/// <summary>
/// Locks the responsive workspace structure: one primary presenter and one inspector
/// presenter inside the host, the approved navigation widths, the minimum viewport, the
/// DIP-based initial window size and the handlers that keep the shell mode in step with the
/// real window width. Runtime behaviour is not inferred from XAML alone: the decisions
/// themselves are covered by WorkspaceLayoutPolicyTests.
/// </summary>
public sealed class ResponsiveWorkspaceStructureTests
{
    private const string DesktopRoot =
        "apps/media-desktop/src/TraceFold.Media.Desktop";

    [Fact]
    public void Host_has_exactly_one_primary_and_one_inspector_presenter()
    {
        XDocument host = XDocument.Load(
            DesktopSource("Controls", "ResponsiveInspectorHost.xaml"));
        XElement[] presenters = [.. host.Descendants()
            .Where(element => element.Name.LocalName == "ContentPresenter")];

        Assert.Equal(2, presenters.Length);
        Assert.Single(presenters, element => Attribute(element, "Name") == "PrimaryPresenter");
        Assert.Single(presenters, element => Attribute(element, "Name") == "InspectorPresenter");
    }

    [Fact]
    public void Host_names_the_inspector_column_and_surface()
    {
        XDocument host = XDocument.Load(
            DesktopSource("Controls", "ResponsiveInspectorHost.xaml"));

        Assert.Single(host.Descendants(), element =>
            element.Name.LocalName == "ColumnDefinition"
            && Attribute(element, "Name") == "InspectorColumn");
        Assert.Single(host.Descendants(), element =>
            element.Name.LocalName == "Border"
            && Attribute(element, "Name") == "InspectorSurface");
    }

    [Fact]
    public void Host_is_presentation_only_and_exposes_drawer_members()
    {
        XDocument host = XDocument.Load(
            DesktopSource("Controls", "ResponsiveInspectorHost.xaml"));
        Assert.DoesNotContain("ViewModel", host.ToString(), StringComparison.Ordinal);

        string code = File.ReadAllText(
            DesktopSource("Controls", "ResponsiveInspectorHost.xaml.cs"));
        foreach (string member in new[]
        {
            "PrimaryContent",
            "InspectorContent",
            "HasInspectorContent",
            "IsDrawerOpen",
            "LayoutMode",
            "OpenDrawer",
            "CloseDrawer",
            "LayoutModeChanged",
            "DrawerOpened",
            "DrawerClosed",
        })
        {
            Assert.Contains(member, code, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("TraceFold.Media.Application.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceFold.Media.Domain.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceFold.Media.Infrastructure.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_uses_approved_navigation_widths()
    {
        XDocument shell = XDocument.Load(DesktopSource("Shell", "ShellPage.xaml"));
        XElement navigation = ElementByName(shell, "NavigationView");

        Assert.Equal("{StaticResource TraceFoldNavigationOpenPaneWidth}",
            Attribute(navigation, "OpenPaneLength"));
        Assert.Equal("{StaticResource TraceFoldNavigationCompactPaneWidth}",
            Attribute(navigation, "CompactPaneLength"));
    }

    [Fact]
    public void Shell_keeps_one_frame_and_the_four_destinations()
    {
        XDocument shell = XDocument.Load(DesktopSource("Shell", "ShellPage.xaml"));

        Assert.Single(shell.Descendants(), element => element.Name.LocalName == "Frame");
        string[] ids = [.. shell.Descendants()
            .Select(element => AutomationId(element))
            .Where(value => value is not null)
            .Select(value => value!)];

        Assert.Contains("TraceFoldShell", ids);
        Assert.Equal(
            ["NavInbox", "NavLibrary", "NavSettings", "NavTaxonomy"],
            ids.Where(id => id.StartsWith("Nav", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Shell_updates_navigation_mode_from_loaded_and_size_changed()
    {
        string code = File.ReadAllText(DesktopSource("Shell", "ShellPage.xaml.cs"));

        Assert.Contains("private void ApplyNavigationMode(double", code, StringComparison.Ordinal);
        Assert.Contains("WorkspaceLayoutPolicy.GetMode(", code, StringComparison.Ordinal);
        Assert.Contains("Loaded +=", code, StringComparison.Ordinal);
        Assert.Contains("SizeChanged +=", code, StringComparison.Ordinal);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftCompact", code, StringComparison.Ordinal);
        Assert.Contains("NavigationViewPaneDisplayMode.Left", code, StringComparison.Ordinal);

        int start = code.IndexOf("private void ApplyNavigationMode(double", StringComparison.Ordinal);
        int end = code.IndexOf("private ", start + 10, StringComparison.Ordinal);
        string body = end > start ? code[start..end] : code[start..];
        Assert.DoesNotContain("ViewModel", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_declares_minimum_viewport_and_stable_root_ids()
    {
        XDocument window = XDocument.Load(DesktopSource("MainWindow.xaml"));

        Assert.Single(window.Descendants(), element =>
            AutomationId(element) == "TraceFoldMainWindow");
        Assert.Equal("{StaticResource TraceFoldMinViewportWidth}",
            Attribute(ElementByName(window, "Grid"), "MinWidth"));
        Assert.Equal("{StaticResource TraceFoldMinViewportHeight}",
            Attribute(ElementByName(window, "Grid"), "MinHeight"));
    }

    [Fact]
    public void Main_window_resizes_in_dip_through_the_shared_conversion()
    {
        string code = File.ReadAllText(DesktopSource("MainWindow.xaml.cs"));

        Assert.Contains("const double InitialWidthDip = 1280", code, StringComparison.Ordinal);
        Assert.Contains("const double InitialHeightDip = 800", code, StringComparison.Ordinal);
        Assert.Contains("WorkspaceLayoutPolicy.PhysicalPixels(", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Resize(", code, StringComparison.Ordinal);
        Assert.Contains("RasterizationScale", code, StringComparison.Ordinal);
    }

    private static XElement ElementByName(XDocument document, string localName) =>
        document.Descendants().First(element => element.Name.LocalName == localName);

    private static string? AutomationId(XElement element) =>
        Attribute(element, "AutomationProperties.AutomationId");

    private static string? Attribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name)
            ?.Value;

    private static string DesktopSource(params string[] parts)
    {
        string relative = Path.Combine(parts);
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, DesktopRoot, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Desktop source is missing: " + relative);
    }
}
