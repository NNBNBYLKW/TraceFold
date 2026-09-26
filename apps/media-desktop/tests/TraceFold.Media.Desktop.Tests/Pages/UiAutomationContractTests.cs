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
        XElement? direct = page.Descendants().FirstOrDefault(element =>
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
}
