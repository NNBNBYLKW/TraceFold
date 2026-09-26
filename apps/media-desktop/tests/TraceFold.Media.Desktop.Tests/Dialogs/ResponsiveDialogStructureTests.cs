using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Dialogs;

public sealed class ResponsiveDialogStructureTests
{
    [Theory]
    [InlineData("BatchMetadataDialog", "BatchMetadataScrollRoot", 7, "BatchMetadataSubmitButton")]
    [InlineData("BatchIngestConfirmationDialog", "BatchIngestScrollRoot", 2, "BatchIngestConfirmButton")]
    [InlineData("ConflictComparisonDialog", "ConflictScrollRoot", 3, "ConflictSkipButton,ConflictKeepBothButton,ConflictReplaceButton")]
    [InlineData("LibraryBatchEditDialog", "LibraryBatchEditScrollRoot", 14, "LibraryNamingSavePresetButton,LibraryNamingDeletePresetButton,LibraryNamingOverwritePresetButton,LibraryNamingAddRuleButton,LibraryBatchCancelButton,LibraryBatchApplyButton")]
    [InlineData("TaxonomyMergeDialog", "TaxonomyMergeScrollRoot", 2, "TaxonomyMergeCancelButton,TaxonomyMergeApplyButton")]
    public void Dialog_has_reachable_scroll_boundary_and_preserves_actions(
        string name, string scrollName, int buttonCount, string requiredButtonIds)
    {
        XDocument document = XDocument.Load(DialogSource(name + ".xaml"));
        XElement root = document.Root!;
        XElement scroll = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer"
            && Attribute(element, "Name") == scrollName);
        Assert.Equal("Auto", Attribute(scroll, "VerticalScrollBarVisibility"));
        Assert.Equal(buttonCount, document.Descendants().Count(element =>
            element.Name.LocalName == "Button"));
        Assert.True(NumericAttribute(root, "MinWidth") <= 900);
        Assert.DoesNotContain(root.Descendants(), element =>
            element.Name.LocalName == "Double"
            && Attribute(element, "Key") == "ContentDialogMinWidth"
            && double.TryParse(element.Value, out double width) && width > 900);
        Assert.DoesNotContain(root.Descendants(), element =>
            element.Name.LocalName == "Grid"
            && NumericAttribute(element, "MaxHeight") >= 650);
        Assert.Contains("MaxHeight", File.ReadAllText(DialogSource(name + ".xaml.cs")),
            StringComparison.Ordinal);
        foreach (string id in requiredButtonIds.Split(','))
        {
            XElement button = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "Button"
                && Attribute(element, "AutomationProperties.AutomationId") == id);
            if (id is "BatchMetadataSubmitButton" or "BatchIngestConfirmButton"
                or "ConflictSkipButton" or "ConflictKeepBothButton"
                or "ConflictReplaceButton" or "LibraryBatchCancelButton"
                or "LibraryBatchApplyButton" or "TaxonomyMergeCancelButton"
                or "TaxonomyMergeApplyButton")
            {
                Assert.DoesNotContain(scroll.Descendants(), element => element == button);
            }
        }
    }

    [Theory]
    [InlineData("ConflictComparisonDialog", "ConflictScrollRoot", "ConflictSkipButton", "ConflictKeepBothButton", "ConflictReplaceButton")]
    [InlineData("LibraryBatchEditDialog", "LibraryBatchEditScrollRoot", "LibraryBatchCancelButton", "LibraryBatchApplyButton", "LibraryNamingAddRuleButton")]
    [InlineData("TaxonomyMergeDialog", "TaxonomyMergeScrollRoot", "TaxonomyMergeCancelButton", "TaxonomyMergeApplyButton", "TaxonomyMergeAffectedFiles")]
    public void Large_dialog_has_both_active_layout_states_and_actions(
        string name, string scrollName, string firstId, string secondId, string thirdId)
    {
        XDocument document = XDocument.Load(DialogSource(name + ".xaml"));
        XElement visualRoot = document.Root!.Elements().First(element =>
            element.Name.LocalName is "Grid" or "ScrollViewer");
        Assert.Contains(visualRoot.Elements(), element =>
            element.Name.LocalName == "VisualStateManager.VisualStateGroups");
        foreach ((string stateName, string width) in new[]
        {
            ("CompactDialogLayout", "0"), ("WideDialogLayout", "1280")
        })
        {
            XElement state = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "VisualState"
                && Attribute(element, "Name") == stateName);
            Assert.Contains(state.Descendants(), element =>
                element.Name.LocalName == "AdaptiveTrigger"
                && Attribute(element, "MinWindowWidth") == width);
        }

        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer"
            && Attribute(element, "Name") == scrollName);
        foreach (string id in new[] { firstId, secondId, thirdId })
        {
            Assert.Contains(document.Descendants(), element =>
                Attribute(element, "AutomationProperties.AutomationId") == id);
        }
    }

    [Theory]
    [InlineData("ConflictComparisonDialog", "ConflictScrollRoot", "IncomingPane:0,ExistingPane:1")]
    [InlineData("LibraryBatchEditDialog", "LibraryBatchEditScrollRoot", "MetadataModePane:0,MetadataEditorPane:1,NamingPresetPane:0,NamingRulesPane:1,NamingPreviewPane:2")]
    [InlineData("TaxonomyMergeDialog", "TaxonomyMergeScrollRoot", "SourcePane:0,TargetPane:1,PreviewPane:2")]
    public void Compact_layout_stacks_original_panes_inside_one_scroll_boundary(
        string name, string scrollName, string paneRows)
    {
        XDocument document = XDocument.Load(DialogSource(name + ".xaml"));
        XElement scroll = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ScrollViewer"
            && Attribute(element, "Name") == scrollName);
        XElement compact = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "VisualState"
            && Attribute(element, "Name") == "CompactDialogLayout");

        foreach (string paneRow in paneRows.Split(','))
        {
            string[] parts = paneRow.Split(':');
            Assert.Contains(scroll.Descendants(), element =>
                Attribute(element, "Name") == parts[0]);
            if (parts[1] != "0")
            {
                Assert.Contains(compact.Descendants(), element =>
                    element.Name.LocalName == "Setter"
                    && Attribute(element, "Target") == $"{parts[0]}.(Grid.Row)"
                    && Attribute(element, "Value") == parts[1]);
            }
        }
    }

    private static double NumericAttribute(XElement element, string name) =>
        double.TryParse(Attribute(element, name), out double value) ? value : 0;

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == name)?.Value;

    private static string DialogSource(string filename)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "apps", "media-desktop",
                "src", "TraceFold.Media.Desktop", "Dialogs", filename);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException("Dialog source is missing: " + filename);
    }
}
