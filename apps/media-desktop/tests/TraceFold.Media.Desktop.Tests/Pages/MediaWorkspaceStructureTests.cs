using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class MediaWorkspaceStructureTests
{
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
            "InboxBatchMetadataButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                batchDialogXaml + inboxXaml,
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
        double width = double.Parse(
            openPaneLength.Value,
            CultureInfo.InvariantCulture);
        Assert.InRange(width, 180, 240);
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
    public async Task Taxonomy_and_settings_pages_expose_stable_stage_two_controls()
    {
        string taxonomy = await File.ReadAllTextAsync(
            DesktopSource("Pages", "TaxonomyPage.xaml"),
            TestContext.Current.CancellationToken);
        string settings = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SettingsPage.xaml"),
            TestContext.Current.CancellationToken);
        string taxonomyCode = await File.ReadAllTextAsync(
            DesktopSource("Pages", "TaxonomyPage.xaml.cs"),
            TestContext.Current.CancellationToken);

        foreach (string id in new[]
        {
            "TaxonomyKindSelector",
            "TaxonomySearchBox",
            "TaxonomyList",
            "TaxonomyCreateButton",
            "TaxonomyRenameButton",
            "TaxonomyDeleteButton"
        })
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                taxonomy,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "IsEnabled=\"{x:Bind ViewModel.CanRename, Mode=OneWay}\"",
            taxonomy,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEnabled=\"{x:Bind ViewModel.CanDelete, Mode=OneWay}\"",
            taxonomy,
            StringComparison.Ordinal);

        Assert.Contains(
            "AutomationProperties.AutomationId=\"AutoScanToggle\"",
            settings,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"LastScanStatus\"",
            settings,
            StringComparison.Ordinal);
        Assert.Contains("RunGuardedAsync", taxonomyCode, StringComparison.Ordinal);
        Assert.Contains("Unloaded += OnUnloaded", taxonomyCode, StringComparison.Ordinal);
        Assert.Contains(
            "TaxonomyPageOperationFailed",
            taxonomyCode,
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
}
