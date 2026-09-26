using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

/// <summary>
/// Locks the static UI Automation contract before any visual migration: every logical
/// control that the acceptance toolkit drives has one language-independent static
/// AutomationId, a concise Chinese accessible name, and keeps its native WinUI control
/// type so the expected UIA pattern stays available.
/// </summary>
public sealed class UiAutomationContractTests
{
    private const string ProductRoot =
        "apps/media-desktop/src/TraceFold.Media.Desktop";
    private const string ManifestPath =
        "apps/media-desktop/tools/ui-acceptance-contract/stage5cc-uia-contract.json";

    private static readonly string[] ManifestFiles =
    [
        "MainWindow.xaml", "Shell/ShellPage.xaml",
        "Pages/InboxPage.xaml", "Pages/LibraryPage.xaml",
        "Pages/TaxonomyPage.xaml", "Pages/SettingsPage.xaml",
        "Dialogs/BatchMetadataDialog.xaml",
        "Dialogs/BatchIngestConfirmationDialog.xaml",
        "Dialogs/ConflictComparisonDialog.xaml",
        "Dialogs/LibraryBatchEditDialog.xaml",
        "Dialogs/TaxonomyMergeDialog.xaml",
    ];

    private static readonly string[] RequiredAfterMigration =
    [
        "TraceFoldMainWindow", "TraceFoldShell", "NavInbox", "NavLibrary",
        "NavTaxonomy", "NavSettings", "InboxPageRoot", "LibraryPageRoot",
        "TaxonomyPageRoot", "SettingsPageRoot", "InboxSelectedCount",
        "LibrarySelectedCount", "TaxonomySelectedCount",
        "InboxInspectorOpenButton", "InboxInspectorCloseButton", "InspectorPane",
        "LibraryInspectorOpenButton", "LibraryInspectorCloseButton",
        "LibraryInspectorPane", "TaxonomyInspectorOpenButton",
        "TaxonomyInspectorCloseButton", "TaxonomyInspectorPane",
        "InboxListHeader", "LibraryListHeader", "BatchMetadataScrollRoot",
        "BatchIngestScrollRoot", "ConflictScrollRoot",
        "LibraryBatchEditScrollRoot", "TaxonomyMergeScrollRoot",
        "ConflictSkipButton", "ConflictKeepBothButton", "ConflictReplaceButton",
        "InboxBatchMetadataButton", "InboxBatchIngestButton",
        "LibraryBatchApplyButton", "LibraryBatchUndoButton",
        "LibraryNamingPreviewList", "TaxonomyMergeApplyButton",
        "TaxonomyDeleteButton", "SettingsTabs", "SystemDiagnosticsTab",
        "DiagnosticsOverallStatus", "DiagnosticsLastChecked",
        "InboxOperationStatus", "InboxRecoveryStatus", "LibraryStatusMessage",
        "LastScanStatus", "ProcessingToolStatus", "SettingsStatusMessage",
        "TaxonomyMergeStatus", "LibraryBatchDialogStatus",
    ];

    [Fact]
    public void Shared_manifest_covers_every_static_id_with_exact_native_semantics()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(FindFile(ManifestPath)));
        JsonElement entries = manifest.RootElement.GetProperty("entries");
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string file = entry.GetProperty("file").GetString()!;
            string id = entry.GetProperty("automationId").GetString()!;
            string name = entry.GetProperty("name").GetString()!;
            string type = entry.GetProperty("controlType").GetString()!;
            string scope = entry.GetProperty("scope").GetString()!;
            string policy = entry.GetProperty("dynamicSelectorPolicy").GetString()!;
            Assert.Contains(file, ManifestFiles);
            Assert.True(seen.Add(id), "Duplicate manifest AutomationId: " + id);
            Assert.Matches("[\u4e00-\u9fff]", name);
            Assert.False(string.IsNullOrWhiteSpace(scope));
            Assert.Equal("static_id", policy);
            JsonElement patterns = entry.GetProperty("requiredPatterns");
            Assert.Equal(JsonValueKind.Array, patterns.ValueKind);
            foreach (JsonElement pattern in patterns.EnumerateArray())
            {
                Assert.Contains(pattern.GetString(), new[]
                {
                    "Invoke", "Toggle", "Value", "Selection", "SelectionItem",
                    "ExpandCollapse", "Scroll", "Window",
                });
            }
            if (type == "Button")
            {
                Assert.Contains(patterns.EnumerateArray(), pattern =>
                    pattern.GetString() == "Invoke");
            }
            if (id is "InspectorPane" or "LibraryInspectorPane" or "TaxonomyInspectorPane")
            {
                Assert.StartsWith("page_host:", scope);
            }

            XDocument page = LoadProductXaml(file);
            int occurrences = page.Root!.DescendantsAndSelf().Count(element =>
                element.Attribute("AutomationProperties.AutomationId")?.Value == id)
                + page.Root!.DescendantsAndSelf().Count(element =>
                    element.Attribute("InputAutomationId")?.Value == id
                    || element.Attribute("SelectedTermsAutomationId")?.Value == id);
            Assert.Equal(1, occurrences);
            (XElement element, string actualName) = ResolveControl(file, id);
            Assert.Equal(type, element.Name.LocalName);
            Assert.Equal(name, actualName);
        }

        foreach (string file in ManifestFiles)
        {
            XDocument xaml = LoadProductXaml(file);
            string[] ids = [.. xaml.Root!.DescendantsAndSelf()
                .Select(element => element.Attribute("AutomationProperties.AutomationId")?.Value)
                .Concat(xaml.Root!.DescendantsAndSelf().SelectMany(element => new[]
                {
                    element.Attribute("InputAutomationId")?.Value,
                    element.Attribute("SelectedTermsAutomationId")?.Value,
                }))
                .Where(id => !string.IsNullOrWhiteSpace(id) && !id!.StartsWith('{'))
                .Select(id => id!)];
            Assert.All(ids, id => Assert.Contains(id, seen));
        }

        Assert.All(RequiredAfterMigration, id => Assert.Contains(id, seen));
    }

    [Fact]
    public void Dynamic_row_policy_uses_fixed_accessible_names_without_guid_selectors()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(FindFile(ManifestPath)));
        JsonElement rows = manifest.RootElement.GetProperty("dynamicRows");
        Assert.Equal(2, rows.GetArrayLength());
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string file = row.GetProperty("file").GetString()!;
            string fixedName = row.GetProperty("fixedAccessibleName").GetString()!;
            Assert.Contains(file, new[] { "Pages/InboxPage.xaml", "Pages/LibraryPage.xaml" });
            Assert.False(string.IsNullOrWhiteSpace(fixedName));
            Assert.DoesNotContain("guid", fixedName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("accessible_name", row.GetProperty("selector").GetString());
            Assert.True(row.GetProperty("forbidGuidSelectors").GetBoolean());
            Assert.Contains(row.GetProperty("fixedFilename").GetString()!, fixedName);
            Assert.Equal("{x:Bind AccessibleName, Mode=OneWay}",
                LoadProductXaml(file).Descendants().Single(element =>
                    element.Attribute("AutomationProperties.AutomationId")?.Value
                        == "{x:Bind AutomationId}")
                    .Attribute("AutomationProperties.Name")?.Value);
        }
    }

    [Theory]
    [InlineData("Pages/InboxPage.xaml", "InspectorPane")]
    [InlineData("Pages/LibraryPage.xaml", "LibraryInspectorPane")]
    [InlineData("Pages/TaxonomyPage.xaml", "TaxonomyInspectorPane")]
    public void Shared_inspector_uses_the_page_owned_id(string file, string inspectorId)
    {
        XDocument page = LoadProductXaml(file);
        XElement host = Assert.Single(page.Descendants(), element =>
            element.Name.LocalName == "ResponsiveInspectorHost");
        Assert.Null(host.Attribute("AutomationProperties.AutomationId"));
        XElement inspector = Assert.Single(host.Descendants(), element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == inspectorId);
        Assert.Contains(inspector.Ancestors(), element =>
            element.Name.LocalName == "ResponsiveInspectorHost.InspectorContent");
    }

    [Fact]
    public void Programmatic_delete_confirmation_is_in_the_shared_contract()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(FindFile(ManifestPath)));
        JsonElement controls = manifest.RootElement.GetProperty("runtimeControls");
        string source = File.ReadAllText(FindFile(ProductRoot
            + "/Dialogs/WindowsTaxonomyMaintenanceDialogService.cs"));
        Assert.Equal(2, controls.GetArrayLength());
        foreach (JsonElement control in controls.EnumerateArray())
        {
            string id = control.GetProperty("automationId").GetString()!;
            string name = control.GetProperty("name").GetString()!;
            Assert.Contains(id, new[]
            {
                "TaxonomyDeleteConfirmDialog", "TaxonomyDeleteTermList",
            });
            Assert.Matches("[\u4e00-\u9fff]", name);
            Assert.Contains($"AutomationProperties.SetAutomationId({(id == "TaxonomyDeleteConfirmDialog" ? "dialog" : "terms")}, \"{id}\")", source);
            Assert.Contains($"AutomationProperties.SetName({(id == "TaxonomyDeleteConfirmDialog" ? "dialog" : "terms")}, \"{name}\")", source);
            Assert.Equal("dialog:taxonomy_delete", control.GetProperty("scope").GetString());
            Assert.Equal("static_id", control.GetProperty("dynamicSelectorPolicy").GetString());
        }
    }

    [Theory]
    [InlineData("InboxPage.xaml", "InboxOperationStatus", "待入库操作状态")]
    [InlineData("LibraryPage.xaml", "LibraryStatusMessage", "正式文件库状态消息")]
    [InlineData("SettingsPage.xaml", "SettingsStatusMessage", "设置状态消息")]
    [InlineData("InboxPage.xaml", "InboxAuthorInput", "输入待入库资料的作者")]
    [InlineData("InboxPage.xaml", "InboxAuthorInputTerms", "已添加的待入库资料作者")]
    [InlineData("InboxPage.xaml", "InboxSeriesInput", "输入待入库资料的系列")]
    [InlineData("InboxPage.xaml", "InboxSeriesInputTerms", "已添加的待入库资料系列")]
    [InlineData("InboxPage.xaml", "InboxTagInput", "输入待入库资料的内容标签")]
    [InlineData("InboxPage.xaml", "InboxTagInputTerms", "已添加的待入库资料内容标签")]
    [InlineData("LibraryPage.xaml", "LibraryAuthorInput", "输入正式文件库资料的作者")]
    [InlineData("LibraryPage.xaml", "LibraryAuthorInputTerms", "已添加的正式文件库资料作者")]
    [InlineData("LibraryPage.xaml", "LibrarySeriesInput", "输入正式文件库资料的系列")]
    [InlineData("LibraryPage.xaml", "LibrarySeriesInputTerms", "已添加的正式文件库资料系列")]
    [InlineData("LibraryPage.xaml", "LibraryTagInput", "输入正式文件库资料的内容标签")]
    [InlineData("LibraryPage.xaml", "LibraryTagInputTerms", "已添加的正式文件库资料内容标签")]
    public void Task_zero_name_stays_exact(string file, string id, string name) =>
        Assert.Equal(name, ResolveControl(file, id).Name);

    [Theory]
    [InlineData("Pages/InboxPage.xaml")]
    [InlineData("Pages/LibraryPage.xaml")]
    [InlineData("Pages/TaxonomyPage.xaml")]
    [InlineData("Dialogs/BatchIngestConfirmationDialog.xaml")]
    [InlineData("Dialogs/BatchMetadataDialog.xaml")]
    [InlineData("Dialogs/ConflictComparisonDialog.xaml")]
    [InlineData("Dialogs/LibraryBatchEditDialog.xaml")]
    [InlineData("Dialogs/TaxonomyMergeDialog.xaml")]
    public void Disabled_buttons_explain_the_required_action(string file)
    {
        XElement[] buttons = [.. LoadProductXaml(file).Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && element.Attribute("IsEnabled") is not null
                && element.Attribute("AutomationProperties.AutomationId") is { Value: var id }
                && !id.StartsWith('{'))];
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Matches("[\u4e00-\u9fff]",
            button.Attribute("AutomationProperties.HelpText")?.Value ?? string.Empty));
    }

    public static TheoryData<string, string, string> RequiredControls => new()
    {
        { "InboxPage.xaml", "InboxAuthorInput", "AutoSuggestBox" },
        { "InboxPage.xaml", "InboxOperationStatus", "TextBlock" },
        { "LibraryPage.xaml", "LibraryAuthorInput", "AutoSuggestBox" },
        { "LibraryPage.xaml", "LibraryStatusMessage", "TextBlock" },
        { "SettingsPage.xaml", "SettingsStatusMessage", "TextBlock" },
    };

    [Theory]
    [MemberData(nameof(RequiredControls))]
    public void Required_control_has_stable_id_name_and_native_control(
        string file,
        string id,
        string expectedElement)
    {
        (XElement element, string name) = ResolveControl(file, id);

        Assert.Equal(expectedElement, element.Name.LocalName);
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.Matches("[\u4e00-\u9fff]", name);
    }

    [Theory]
    [InlineData("InboxPage.xaml")]
    [InlineData("LibraryPage.xaml")]
    [InlineData("TaxonomyPage.xaml")]
    [InlineData("SettingsPage.xaml")]
    [InlineData("Dialogs/BatchMetadataDialog.xaml")]
    [InlineData("Dialogs/LibraryBatchEditDialog.xaml")]
    public void Static_automation_ids_are_unique_within_a_xaml_file(string file)
    {
        string[] ids = [.. LoadProductXaml(file)
            .Descendants()
            .Select(element => element.Attribute("AutomationProperties.AutomationId")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value!.StartsWith('{'))
            .Select(value => value!)];

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("InboxPage.xaml", "InboxAuthorInput", "InboxSeriesInput", "InboxTagInput")]
    [InlineData("LibraryPage.xaml", "LibraryAuthorInput", "LibrarySeriesInput", "LibraryTagInput")]
    public void Taxonomy_picker_use_sites_supply_distinct_input_and_term_ids(
        string file,
        string authorId,
        string seriesId,
        string tagId)
    {
        XDocument page = LoadProductXaml(file);
        XElement[] pickers = [.. page.Descendants()
            .Where(element => element.Name.LocalName == "TaxonomyPickerControl")];

        Assert.Equal(3, pickers.Length);
        string[] expected = [authorId, seriesId, tagId];
        for (int index = 0; index < pickers.Length; index++)
        {
            Assert.Equal(expected[index], Required(pickers[index], "InputAutomationId"));
            Assert.Matches("[\u4e00-\u9fff]", Required(pickers[index], "InputAutomationName"));
            Assert.False(string.IsNullOrWhiteSpace(
                Required(pickers[index], "SelectedTermsAutomationId")));
            Assert.Matches("[\u4e00-\u9fff]", Required(pickers[index], "SelectedTermsAutomationName"));
        }
    }

    [Fact]
    public void Taxonomy_picker_ids_are_unique_across_pages_and_dialogs()
    {
        string[] files =
        [
            "InboxPage.xaml",
            "LibraryPage.xaml",
            "TaxonomyPage.xaml",
            "Dialogs/BatchMetadataDialog.xaml",
            "Dialogs/LibraryBatchEditDialog.xaml",
        ];
        string[] ids = [.. files
            .SelectMany(file => LoadProductXaml(file).Descendants()
                .Where(element => element.Name.LocalName == "TaxonomyPickerControl")
                .SelectMany(element => new[]
                {
                    element.Attribute("InputAutomationId")?.Value,
                    element.Attribute("SelectedTermsAutomationId")?.Value,
                }))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)];

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Taxonomy_picker_inner_controls_expose_the_parent_supplied_ids()
    {
        XDocument picker = LoadProductXaml("Controls/TaxonomyPickerControl.xaml");
        XElement input = picker.Descendants()
            .Single(element => element.Name.LocalName == "AutoSuggestBox");
        XElement terms = picker.Descendants()
            .Single(element => element.Name.LocalName == "ItemsControl");

        Assert.Equal("{x:Bind InputAutomationId, Mode=OneWay}",
            input.Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal("{x:Bind InputAutomationName, Mode=OneWay}",
            input.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal("{x:Bind SelectedTermsAutomationId, Mode=OneWay}",
            terms.Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal("{x:Bind SelectedTermsAutomationName, Mode=OneWay}",
            terms.Attribute("AutomationProperties.Name")?.Value);
    }

    [Fact]
    public void Taxonomy_picker_icon_only_remove_action_has_an_explicit_name()
    {
        XDocument picker = LoadProductXaml("Controls/TaxonomyPickerControl.xaml");
        XElement[] iconButtons = [.. picker.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => (element.Attribute("FontFamily")?.Value ?? string.Empty)
                .Contains("Segoe Fluent Icons", StringComparison.Ordinal))];

        Assert.NotEmpty(iconButtons);
        Assert.All(iconButtons, button =>
            Assert.Matches("[\u4e00-\u9fff]", AutomationName(button)));
    }

    [Theory]
    [InlineData("InboxPage.xaml")]
    [InlineData("LibraryPage.xaml")]
    public void Media_row_binds_its_deterministic_accessible_name(string file)
    {
        XElement row = LoadProductXaml(file).Descendants().Single(element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value
                == "{x:Bind AutomationId}");

        Assert.Equal("{x:Bind AccessibleName, Mode=OneWay}",
            row.Attribute("AutomationProperties.Name")?.Value);
    }

    /// <summary>
    /// Resolves the effective control behind an automation id and the accessible name that
    /// belongs to it. A taxonomy picker input id lives on the use site and its accessible
    /// name belongs to that site, while the native control stays the picker's inner one.
    /// </summary>
    private static (XElement Element, string Name) ResolveControl(string file, string id)
    {
        XDocument page = LoadProductXaml(file);
        XElement? direct = page.Root!.DescendantsAndSelf().FirstOrDefault(element =>
            element.Attribute("AutomationProperties.AutomationId")?.Value == id);
        if (direct is not null)
        {
            return (direct, AutomationName(direct));
        }

        XDocument picker = LoadProductXaml("Controls/TaxonomyPickerControl.xaml");
        XElement? site = page.Descendants().FirstOrDefault(element =>
            element.Name.LocalName == "TaxonomyPickerControl"
            && (element.Attribute("InputAutomationId")?.Value == id
                || element.Attribute("SelectedTermsAutomationId")?.Value == id));
        if (site is null)
        {
            Assert.Fail("No control defines AutomationId '" + id + "' in " + file + ".");
        }

        bool input = site.Attribute("InputAutomationId")?.Value == id;
        XElement effective = picker.Descendants().Single(element => element.Name.LocalName
            == (input ? "AutoSuggestBox" : "ItemsControl"));
        return (effective, Required(site, input
            ? "InputAutomationName"
            : "SelectedTermsAutomationName"));
    }

    private static string Required(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value
        ?? throw new Xunit.Sdk.XunitException(
            "The taxonomy picker use site is missing " + attribute + ".");

    private static string AutomationName(XElement element) =>
        element.Attribute("AutomationProperties.Name")?.Value ?? string.Empty;

    private static XDocument LoadProductXaml(string file)
    {
        string relative = file.Replace('/', Path.DirectorySeparatorChar);
        string[] candidates = relative.Contains(Path.DirectorySeparatorChar)
            ? [relative]
            :
            [
                Path.Combine("Pages", relative),
                Path.Combine("Controls", relative),
                Path.Combine("Dialogs", relative),
                relative,
            ];
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            foreach (string candidate in candidates)
            {
                string path = Path.Combine(current.FullName, ProductRoot, candidate);
                if (File.Exists(path))
                {
                    return XDocument.Load(path);
                }
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Product XAML is missing: " + file);
    }

    private static string FindFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("UIA contract is missing: " + relativePath);
    }
}
