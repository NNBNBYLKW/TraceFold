using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class SettingsDiagnosticsStructureTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void Settings_uses_two_fixed_native_tabs_with_folder_settings_first()
    {
        XDocument document = XDocument.Load(
            DesktopSource("Pages", "SettingsPage.xaml"));
        XElement tabs = ElementByAutomationId(document, "SettingsTabs");

        Assert.Equal("TabView", tabs.Name.LocalName);
        Assert.Equal("False", Attribute(tabs, "IsAddTabButtonVisible"));
        Assert.Equal("0", Attribute(tabs, "SelectedIndex"));

        XElement[] items = tabs.Elements(Presentation + "TabViewItem").ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("FolderSettingsTab", AutomationId(items[0]));
        Assert.Equal("SystemDiagnosticsTab", AutomationId(items[1]));
        Assert.All(items, item =>
            Assert.Equal("False", Attribute(item, "IsClosable")));
        Assert.Equal("文件夹与扫描", Attribute(items[0], "Header"));
        Assert.Equal("系统诊断", Attribute(items[1], "Header"));
    }

    [Fact]
    public void Settings_preserves_folder_controls_and_exposes_diagnostic_header()
    {
        XDocument document = XDocument.Load(
            DesktopSource("Pages", "SettingsPage.xaml"));
        string[] pageIds =
        [
            "SettingsTabs", "FolderSettingsTab", "SystemDiagnosticsTab",
            "DiagnosticsOverallStatus", "DiagnosticsLastChecked",
            "DiagnosticsRefreshButton", "DiagnosticsCopySummaryButton"
        ];
        string[] existingIds =
        [
            "InboxFolderPath", "PickInboxFolder", "LibraryFolderPath",
            "PickLibraryFolder", "WorkFolderPath", "PickWorkFolder",
            "LastScanStatus", "AutoScanToggle", "ProcessingToolStatus",
            "SettingsValidationErrors", "SaveFolderSettings"
        ];

        foreach (string id in pageIds.Concat(existingIds))
        {
            _ = Assert.Single(ElementsByAutomationId(document, id));
        }

        AssertChineseAccessibleName(
            ElementByAutomationId(document, "DiagnosticsRefreshButton"));
        AssertChineseAccessibleName(
            ElementByAutomationId(document, "DiagnosticsCopySummaryButton"));
    }

    [Fact]
    public void Diagnostic_templates_bind_stable_ids_and_accessible_status()
    {
        XDocument document = XDocument.Load(
            DesktopSource("Pages", "SettingsPage.xaml"));
        XElement group = ElementByName(document, "DiagnosticGroupRoot");
        XElement item = ElementByName(document, "DiagnosticItemRoot");
        XElement error = ElementByName(document, "DiagnosticErrorCode");
        XElement action = ElementByName(document, "DiagnosticActionButton");

        Assert.Equal("{x:Bind AutomationId}", AutomationId(group));
        Assert.Equal("{x:Bind AutomationId}", AutomationId(item));
        Assert.Equal("{x:Bind AccessibleName}", AutomationName(item));
        Assert.Equal("{x:Bind ErrorCodeAutomationId}", AutomationId(error));
        Assert.Equal("{x:Bind AutomationId}", AutomationId(action));
        Assert.Equal("{x:Bind AutomationName}", AutomationName(action));

        Assert.NotNull(item.Descendants().SingleOrDefault(element =>
            Attribute(element, "Glyph") == "{x:Bind IconGlyph}"));
        Assert.NotNull(item.Descendants().SingleOrDefault(element =>
            Attribute(element, "Text") == "{x:Bind StatusText}"));
        Assert.NotNull(item.Descendants().SingleOrDefault(element =>
            Attribute(element, "Text") == "{x:Bind Title}"));
    }

    [Fact]
    public async Task Code_behind_activates_cancels_and_focuses_only_registered_fields()
    {
        string code = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SettingsPage.xaml.cs"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "DiagnosticsViewModel.ActivateAsync",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "DiagnosticsViewModel.CancelCurrentRun",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemDiagnosticActionTarget.InboxDirectory => InboxFolderPath",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemDiagnosticActionTarget.LibraryDirectory => LibraryFolderPath",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemDiagnosticActionTarget.WorkDirectory => WorkFolderPath",
            code,
            StringComparison.Ordinal);
        Assert.Contains("folderField.Focus", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Facts", code, StringComparison.Ordinal);
        Assert.DoesNotContain("explorer.exe", code, StringComparison.Ordinal);
    }

    private static XElement ElementByAutomationId(
        XDocument document,
        string id) => Assert.Single(ElementsByAutomationId(document, id));

    private static IEnumerable<XElement> ElementsByAutomationId(
        XDocument document,
        string id) => document.Descendants().Where(element =>
            AutomationId(element) == id);

    private static XElement ElementByName(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element =>
            Attribute(element, "Name") == name);

    private static string? AutomationId(XElement element) =>
        Attribute(element, "AutomationProperties.AutomationId");

    private static string? AutomationName(XElement element) =>
        Attribute(element, "AutomationProperties.Name");

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == localName)?.Value;

    private static void AssertChineseAccessibleName(XElement element)
    {
        string name = Assert.IsType<string>(AutomationName(element));
        Assert.Contains(name, character => character is >= '\u4e00' and <= '\u9fff');
    }

    private static string DesktopSource(
        string folder,
        string filename,
        [CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(testFile)!,
            "..",
            "..",
            "..",
            "src",
            "TraceFold.Media.Desktop",
            folder,
            filename));
}
