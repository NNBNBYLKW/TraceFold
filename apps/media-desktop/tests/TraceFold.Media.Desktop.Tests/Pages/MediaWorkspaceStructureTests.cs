using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class MediaWorkspaceStructureTests
{
    [Fact]
    public void Inbox_uses_one_responsive_inspector_and_fixed_height_list_rows()
    {
        string xaml = File.ReadAllText(DesktopSource("Pages", "InboxPage.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XElement host = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ResponsiveInspectorHost");
        Assert.Equal("WorkspaceHost", (string?)host.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Name"));
        XElement list = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "InboxGrid");
        XElement header = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "InboxListHeader");
        Assert.Equal("32,*,160,110", (string?)header.Attribute("ColumnDefinitions"));
        Assert.Equal("{StaticResource TraceFoldMediaListStyle}", (string?)list.Attribute("Style"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollMode"));
        XElement row = Assert.Single(list.Descendants(), element =>
            element.Name.LocalName == "DataTemplate");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("ColumnDefinitions") == "32,*,160,110");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("Height") == "{StaticResource TraceFoldFileRowHeight}");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("AutomationProperties.Name") == "{x:Bind AccessibleName, Mode=OneWay}");
        Assert.DoesNotContain("ItemsWrapGrid", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"190\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"120,", xaml, StringComparison.Ordinal);

        foreach (string id in new[]
        {
            "InboxListHeader", "InboxInspectorOpenButton", "InboxInspectorCloseButton", "InboxInspectorHeading"
        })
        {
            AssertElementHasAccessibleName(xaml, id);
        }

        XElement heading = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "InboxInspectorHeading");
        Assert.DoesNotContain(heading.Ancestors(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains(
                "InspectorVisibility", StringComparison.Ordinal) == true);

        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        string code = File.ReadAllText(DesktopSource("Pages", "InboxPage.xaml.cs"));
        Assert.Contains("WorkspaceHost.CloseDrawer()", code, StringComparison.Ordinal);
        Assert.Contains("FocusSelectedInboxItem", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(args.ItemContainer, card.AccessibleName)",
            code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridViewItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Taxonomy_uses_one_responsive_inspector_and_compact_buttons()
    {
        string xaml = File.ReadAllText(DesktopSource("Pages", "TaxonomyPage.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XElement host = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ResponsiveInspectorHost");
        Assert.Equal("WorkspaceHost", (string?)host.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Name"));
        Assert.DoesNotContain("ColumnDefinitions=\"176,12,*,12,320\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"176,12,*\"", xaml, StringComparison.Ordinal);

        foreach (string id in new[]
        {
            "TaxonomyInspectorOpenButton", "TaxonomyInspectorCloseButton", "TaxonomyInspectorHeading"
        })
        {
            AssertElementHasAccessibleName(xaml, id);
        }

        XElement open = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "TaxonomyInspectorOpenButton");
        Assert.DoesNotContain(open.Ancestors(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains(
                "InspectorVisibility", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(open.Ancestors(), element =>
            ((string?)element.Attribute("Body"))?.Contains(
                "SingleInspectorVisibility", StringComparison.Ordinal) == true);

        XElement heading = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "TaxonomyInspectorHeading");
        Assert.DoesNotContain(heading.Ancestors(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains(
                "InspectorVisibility", StringComparison.Ordinal) == true);

        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        string code = File.ReadAllText(DesktopSource("Pages", "TaxonomyPage.xaml.cs"));
        Assert.Contains("WorkspaceHost.CloseDrawer()", code, StringComparison.Ordinal);
        Assert.Contains("FocusSelectedTaxonomyItem", code, StringComparison.Ordinal);
        Assert.Contains("ListViewItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridViewItem", code, StringComparison.Ordinal);
        int loadedStart = code.IndexOf("private async void OnLoaded", StringComparison.Ordinal);
        int unloadedStart = code.IndexOf("private void OnUnloaded", StringComparison.Ordinal);
        Assert.True(loadedStart >= 0 && unloadedStart > loadedStart);
        Assert.Contains("UpdateInspectorButtonVisibility()",
            code[loadedStart..unloadedStart], StringComparison.Ordinal);
    }

    [Fact]
    public void Library_uses_one_responsive_inspector_and_fixed_height_list_rows()
    {
        string xaml = File.ReadAllText(DesktopSource("Pages", "LibraryPage.xaml"));
        XDocument document = XDocument.Parse(xaml);
        XElement host = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ResponsiveInspectorHost");
        Assert.Equal("WorkspaceHost", (string?)host.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Name"));
        XElement list = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "ListView"
            && (string?)element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name") == "LibraryGrid");
        Assert.Equal("Extended", (string?)list.Attribute("SelectionMode"));
        XElement header = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "LibraryListHeader");
        Assert.Equal("32,*,180,90", (string?)header.Attribute("ColumnDefinitions"));
        Assert.Equal("{StaticResource TraceFoldMediaListStyle}", (string?)list.Attribute("Style"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollMode"));
        XElement row = Assert.Single(list.Descendants(), element =>
            element.Name.LocalName == "DataTemplate");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("ColumnDefinitions") == "32,*,180,90");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("Height") == "{StaticResource TraceFoldFileRowHeight}");
        Assert.Contains(row.DescendantsAndSelf(), element =>
            (string?)element.Attribute("AutomationProperties.Name") == "{x:Bind AccessibleName, Mode=OneWay}");
        Assert.DoesNotContain("ItemsWrapGrid", list.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"190\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"120,", xaml, StringComparison.Ordinal);

        foreach (string id in new[]
        {
            "LibraryListHeader", "LibraryInspectorOpenButton", "LibraryInspectorCloseButton",
            "LibraryInspectorHeading", "LibraryDisplayNameBox"
        })
        {
            AssertElementHasAccessibleName(xaml, id);
        }

        XElement heading = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "LibraryInspectorHeading");
        Assert.DoesNotContain(heading.Ancestors(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains(
                "SelectionVisibility", StringComparison.Ordinal) == true);
        XElement openButton = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "LibraryInspectorOpenButton");
        Assert.DoesNotContain(openButton.Ancestors(), element =>
            ((string?)element.Attribute("Visibility"))?.Contains(
                "ActiveFiltersVisibility", StringComparison.Ordinal) == true);
        Assert.Equal("*,150,112,Auto,Auto", (string?)openButton.Parent?.Attribute("ColumnDefinitions"));
        Assert.Equal("4", (string?)openButton.Attribute("Grid.Column"));
        XElement summary = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "LibrarySelectionSummary");
        Assert.Equal("1", (string?)summary.Attribute("Grid.Row"));
        XElement inspector = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "LibraryInspectorPane");
        Assert.Contains(inspector.Descendants(), element =>
            element.Name.LocalName == "KeyboardAccelerator"
            && (string?)element.Attribute("Key") == "Escape");

        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        string code = File.ReadAllText(DesktopSource("Pages", "LibraryPage.xaml.cs"));
        Assert.Contains("WorkspaceHost.CloseDrawer()", code, StringComparison.Ordinal);
        Assert.Contains("FocusSelectedLibraryItem", code, StringComparison.Ordinal);
        Assert.Contains("IsFocusWithinLibraryList", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(args.ItemContainer, card.AccessibleName)",
            code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridViewItem", code, StringComparison.Ordinal);
        int layoutStart = code.IndexOf(
            "private void OnWorkspaceLayoutModeChanged", StringComparison.Ordinal);
        Assert.True(layoutStart >= 0, "LibraryPage must react to layout mode changes.");
        Assert.DoesNotContain(
            "ViewModel.LoadAsync",
            code[layoutStart..code.IndexOf("private ", layoutStart + 10, StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inbox_page_contains_native_workspace_and_preview_controls()
    {
        string inboxXaml = await File.ReadAllTextAsync(
            DesktopSource("Pages", "InboxPage.xaml"),
            TestContext.Current.CancellationToken);
        string previewXaml = await File.ReadAllTextAsync(
            DesktopSource("Controls", "MediaPreviewControl.xaml"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxRefreshButton\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxGrid\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Extended\"", inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxBatchBar\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxBatchSummary\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxCancelSelectionButton\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\" Key=\"A\"", inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", inboxXaml, StringComparison.Ordinal);
        string batchDialogXaml = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "BatchMetadataDialog.xaml"),
            TestContext.Current.CancellationToken);
        string confirmationXaml = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "BatchIngestConfirmationDialog.xaml"),
            TestContext.Current.CancellationToken);
        foreach (string id in new[]
        {
            "BatchMetadataDialog",
            "BatchMetadataAddTab",
            "BatchMetadataRemoveTab",
            "BatchMetadataReplaceTab",
            "BatchReplaceAuthorsEnabled",
            "BatchReplaceAuthorsClear",
            "BatchReplaceSeriesEnabled",
            "BatchReplaceSeriesClear",
            "BatchReplaceTagsEnabled",
            "BatchReplaceTagsClear",
            "BatchMetadataSubmitButton",
            "InboxBatchMetadataButton",
            "InboxBatchStartProcessingButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                batchDialogXaml + inboxXaml,
                StringComparison.Ordinal);
        }
        foreach (string id in new[]
        {
            "InboxGrid",
            "InboxBatchBar",
            "InboxBatchSummary",
            "InboxCancelSelectionButton",
            "InboxBatchMetadataButton",
            "InboxBatchStartProcessingButton",
            "InboxBatchIngestButton",
            "InboxBatchIngestProgress",
            "InboxBatchStopAfterCurrentButton",
            "InboxBatchIngestResult",
            "InboxBatchShowIncompleteButton",
            "BatchMetadataDialog",
            "BatchMetadataSubmitButton",
            "BatchIngestConfirmationDialog",
            "BatchIngestConfirmButton"
        })
        {
            AssertElementHasAccessibleName(
                inboxXaml + batchDialogXaml + confirmationXaml,
                id);
        }
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InboxTitle\"",
            inboxXaml,
            StringComparison.Ordinal);
        string inboxCode = await File.ReadAllTextAsync(
            DesktopSource("Pages", "InboxPage.xaml.cs"),
            TestContext.Current.CancellationToken);
        Assert.Contains("OnFocusRequested", inboxCode, StringComparison.Ordinal);
        Assert.Contains("ApplyFocusRequest", inboxCode, StringComparison.Ordinal);
        foreach (string id in new[]
        {
            "InboxBatchIngestButton",
            "BatchIngestConfirmationDialog",
            "BatchIngestConfirmButton",
            "InboxBatchIngestProgress",
            "InboxBatchStopAfterCurrentButton",
            "InboxBatchIngestResult",
            "InboxBatchShowIncompleteButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                inboxXaml + confirmationXaml,
                StringComparison.Ordinal);
        }
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InspectorPane\"",
            inboxXaml,
            StringComparison.Ordinal);
        foreach (string id in new[]
        {
            "InboxStartProcessingButton",
            "InboxCancelProcessingButton",
            "InboxStopProcessingButton",
            "InboxRetryProcessingButton",
            "ResolveInboxConflictButton",
            "RecheckIngestRecoveryButton",
            "RetryIngestCleanupButton",
            "OpenRecoveryLocationButton",
            "InboxRecoveryStatus",
            "InboxProcessingProgress",
            "InboxProcessingStep"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                inboxXaml,
                StringComparison.Ordinal);
        }

        Assert.Contains("MediaPlayerElement", previewXaml, StringComparison.Ordinal);

        XDocument document = XDocument.Parse(inboxXaml);
        XElement progress = Assert.Single(document.Descendants(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && attribute.Value == "InboxBatchIngestProgress"));
        Assert.DoesNotContain(progress.Ancestors(), element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && attribute.Value == "InboxBatchSummary"));
    }

    [Fact]
    public async Task Conflict_dialog_exposes_explicit_decisions_and_comparison_controls()
    {
        string xaml = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "ConflictComparisonDialog.xaml"),
            TestContext.Current.CancellationToken);

        foreach (string id in new[]
        {
            "ConflictComparisonDialog",
            "ConflictIncomingPreview",
            "ConflictMatchList",
            "ConflictSkipButton",
            "ConflictKeepBothButton",
            "ConflictReplaceButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                xaml,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("CloseButtonText=\"跳过\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_owns_navigation_and_one_content_frame_only()
    {
        string shellXaml = await File.ReadAllTextAsync(
            DesktopSource("Shell", "ShellPage.xaml"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, Count(shellXaml, "<Frame"));
        Assert.DoesNotContain(
            "AutomationProperties.AutomationId=\"InspectorPane\"",
            shellXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_navigation_keeps_the_three_column_workspace_compact()
    {
        XDocument shell = XDocument.Load(
            DesktopSource("Shell", "ShellPage.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement navigation = Assert.Single(
            shell.Descendants(presentation + "NavigationView"));
        XAttribute? openPaneLength = navigation.Attribute("OpenPaneLength");

        Assert.NotNull(openPaneLength);
        Assert.Equal(
            "{StaticResource TraceFoldNavigationOpenPaneWidth}",
            openPaneLength.Value);
    }

    [Fact]
    public async Task Library_page_contains_id_based_workspace_without_delete_or_rename()
    {
        string xaml = await File.ReadAllTextAsync(
            DesktopSource("Pages", "LibraryPage.xaml"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibrarySearchBox\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibraryGrid\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibraryInspectorPane\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"RevealAssetButton\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsset", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalFilename", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_page_exposes_filter_sort_and_extended_selection_automation_ids()
    {
        string xaml = File.ReadAllText(
            DesktopSource("Pages", "LibraryPage.xaml"));

        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibraryFilterButton\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibrarySortField\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibrarySortDirection\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LibraryActiveFilters\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Extended\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control\" Key=\"A\"", xaml,
            StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_batch_dialog_has_two_tabs_rules_preview_and_safe_primary_action()
    {
        string xaml = File.ReadAllText(
            DesktopSource("Dialogs", "LibraryBatchEditDialog.xaml"));

        foreach (string id in new[]
        {
            "LibraryBatchEditDialog",
            "LibraryBatchMetadataTab",
            "LibraryBatchNamingTab",
            "LibraryBatchMetadataModeList",
            "LibraryBatchAddAuthorInput",
            "LibraryBatchAddTagInput",
            "LibraryBatchRemoveAuthorList",
            "LibraryBatchRemoveTagList",
            "LibraryBatchReplaceAuthorsToggle",
            "LibraryBatchClearAuthorsToggle",
            "LibraryBatchReplaceAuthorInput",
            "LibraryBatchReplaceSeriesToggle",
            "LibraryBatchClearSeriesToggle",
            "LibraryBatchReplaceSeriesInput",
            "LibraryBatchReplaceTagsToggle",
            "LibraryBatchClearTagsToggle",
            "LibraryBatchReplaceTagInput",
            "LibraryNamingSourceSelector",
            "LibraryNamingPresetList",
            "LibraryNamingPresetNameInput",
            "LibraryNamingSavePresetButton",
            "LibraryNamingDeletePresetButton",
            "LibraryNamingOverwritePresetButton",
            "LibraryNamingRuleKindSelector",
            "LibraryNamingAddRuleButton",
            "LibraryNamingRuleList",
            "LibraryNamingTextParameter",
            "LibraryNamingSearchParameter",
            "LibraryNamingReplacementParameter",
            "LibraryNamingCaseSensitiveParameter",
            "LibraryNamingBracketPairParameter",
            "LibraryNamingCaseModeParameter",
            "LibraryNamingSequenceStartParameter",
            "LibraryNamingSequenceStepParameter",
            "LibraryNamingSequenceDigitsParameter",
            "LibraryNamingSequencePlacementParameter",
            "LibraryNamingSequenceSeparatorParameter",
            "LibraryNamingTemplateParameter",
            "LibraryNamingMultiValueSeparatorParameter",
            "LibraryNamingTemplateSequenceStartParameter",
            "LibraryNamingTemplateSequenceStepParameter",
            "LibraryNamingTemplateSequenceDigitsParameter",
            "LibraryNamingPreviewList",
            "LibraryBatchDialogStatus",
            "LibraryBatchCancelButton",
            "LibraryBatchApplyButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                xaml,
                StringComparison.Ordinal);
            AssertElementHasAccessibleName(xaml, id);
        }

        Assert.Contains(
            "仅修改软件中的资料和显示名称",
            xaml,
            StringComparison.Ordinal);
        foreach (string property in new[]
        {
            "EnabledAutomationId",
            "MoveUpAutomationId",
            "MoveDownAutomationId",
            "DeleteAutomationId"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{{x:Bind {property}}}\"",
                xaml,
                StringComparison.Ordinal);
        }


        Assert.Equal(
            5,
            Count(
                xaml,
                "AutomationProperties.AutomationId=\"{x:Bind RemoveAutomationId}\""));
        Assert.Equal(
            2,
            Count(
                xaml,
                "AutomationProperties.AutomationId=\"{x:Bind SelectionAutomationId}\""));

        XDocument document = XDocument.Parse(xaml);
        string[] staticIds = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute =>
                attribute.Name.LocalName
                    == "AutomationProperties.AutomationId"
                && !attribute.Value.StartsWith("{", StringComparison.Ordinal))
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.Equal(
            staticIds.Length,
            staticIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Library_page_exposes_batch_edit_and_undo_controls()
    {
        string xaml = File.ReadAllText(
            DesktopSource("Pages", "LibraryPage.xaml"));

        foreach (string id in new[]
        {
            "LibraryBatchEditButton",
            "LibraryBatchUndoBanner",
            "LibraryBatchUndoButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                xaml,
                StringComparison.Ordinal);
            AssertElementHasAccessibleName(xaml, id);
        }
    }

    [Fact]
    public async Task Taxonomy_workspace_and_merge_dialog_expose_stage_five_c_controls()
    {
        string taxonomy = await File.ReadAllTextAsync(
            DesktopSource("Pages", "TaxonomyPage.xaml"),
            TestContext.Current.CancellationToken);
        string mergeDialog = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "TaxonomyMergeDialog.xaml"),
            TestContext.Current.CancellationToken);
        string taxonomyCode = await File.ReadAllTextAsync(
            DesktopSource("Pages", "TaxonomyPage.xaml.cs"),
            TestContext.Current.CancellationToken);

        foreach (string id in new[]
        {
            "TaxonomyKindList",
            "TaxonomySearchBox",
            "TaxonomyReferenceFilter",
            "TaxonomySortField",
            "TaxonomySortDirection",
            "TaxonomyCreateButton",
            "TaxonomyRefreshButton",
            "TaxonomyTermList",
            "TaxonomyInspectorPane",
            "TaxonomyMergeButton",
            "TaxonomyDeleteButton",
            "TaxonomyDeleteBlockers",
            "TaxonomyUndoBanner",
            "TaxonomyUndoButton",
            "TaxonomyRenameConflictMergeButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                taxonomy,
                StringComparison.Ordinal);
            AssertElementHasAccessibleName(taxonomy, id);
        }

        Assert.Contains("SelectionMode=\"Extended\"", taxonomy,
            StringComparison.Ordinal);
        Assert.Contains("Key=\"A\" Modifiers=\"Control\"", taxonomy,
            StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", taxonomy, StringComparison.Ordinal);

        foreach (string id in new[]
        {
            "TaxonomyMergeDialog",
            "TaxonomyMergeSources",
            "TaxonomyMergeTargetSearch",
            "TaxonomyMergeTargetResults",
            "TaxonomyMergeRenameToggle",
            "TaxonomyMergeFinalName",
            "TaxonomyMergeAffectedSummary",
            "TaxonomyMergeAffectedFiles",
            "TaxonomyMergeStatus",
            "TaxonomyMergeApplyButton",
            "TaxonomyMergeCancelButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                mergeDialog,
                StringComparison.Ordinal);
            AssertElementHasAccessibleName(mergeDialog, id);
        }

        Assert.Contains("仅修改软件资料，不修改实际文件", mergeDialog,
            StringComparison.Ordinal);
        Assert.Contains("RunGuardedAsync", taxonomyCode, StringComparison.Ordinal);
        Assert.Contains("Unloaded += OnUnloaded", taxonomyCode, StringComparison.Ordinal);
        Assert.Contains(
            "TaxonomyPageOperationFailed",
            taxonomyCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taxonomy_sort_handler_tolerates_xaml_initialization_order()
    {
        string taxonomyCode = await File.ReadAllTextAsync(
            DesktopSource("Pages", "TaxonomyPage.xaml.cs"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "private static bool TryReadTag<T>(ComboBox? selector",
            taxonomyCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (selector is null)",
            taxonomyCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taxonomy_merge_dialog_moves_focus_past_a_prefilled_target()
    {
        string codeBehind = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "TaxonomyMergeDialog.xaml.cs"),
            TestContext.Current.CancellationToken);
        string xaml = await File.ReadAllTextAsync(
            DesktopSource("Dialogs", "TaxonomyMergeDialog.xaml"),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "ViewModel.HasPreselectedTarget",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "AffectedFilesList.Focus",
            codeBehind,
            StringComparison.Ordinal);
        string affectedMarker =
            "AutomationProperties.AutomationId=\"TaxonomyMergeAffectedFiles\"";
        int markerIndex = xaml.IndexOf(affectedMarker, StringComparison.Ordinal);
        int elementStart = xaml.LastIndexOf("<ListView", markerIndex,
            StringComparison.Ordinal);
        int elementEnd = xaml.IndexOf('>', markerIndex);
        Assert.True(elementStart >= 0 && elementEnd > markerIndex);
        Assert.Contains(
            "x:Name=\"AffectedFilesList\"",
            xaml[elementStart..(elementEnd + 1)],
            StringComparison.Ordinal);
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

    private static int Count(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static void AssertElementHasAccessibleName(string xaml, string id)
    {
        string marker = $"AutomationProperties.AutomationId=\"{id}\"";
        int markerIndex = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Missing {id}.");
        int elementStart = xaml.LastIndexOf('<', markerIndex);
        int elementEnd = xaml.IndexOf('>', markerIndex);
        Assert.True(elementStart >= 0 && elementEnd > markerIndex);
        string element = xaml[elementStart..(elementEnd + 1)];
        Assert.Contains(
            "AutomationProperties.Name=\"",
            element,
            StringComparison.Ordinal);
    }
}
