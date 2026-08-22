using System.Runtime.CompilerServices;
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
        Assert.Contains(
            "AutomationProperties.AutomationId=\"InspectorPane\"",
            inboxXaml,
            StringComparison.Ordinal);
        Assert.Contains("MediaPlayerElement", previewXaml, StringComparison.Ordinal);
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
