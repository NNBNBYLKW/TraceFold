using System.Runtime.CompilerServices;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class SettingsDiagnosticsArchitectureTests
{
    [Theory]
    [InlineData("Pages", "SettingsViewModel.cs")]
    [InlineData("Pages", "SystemDiagnosticsViewModel.cs")]
    public async Task ViewModels_do_not_import_infrastructure_or_process_apis(
        string folder,
        string filename)
    {
        string source = await File.ReadAllTextAsync(
            DesktopSource(folder, filename),
            TestContext.Current.CancellationToken);

        foreach (string forbidden in new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.Sqlite",
            "System.Diagnostics.Process",
            "DriveInfo",
            "FileStream",
            "Directory."
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Page_never_uses_diagnostic_facts_as_paths_or_commands()
    {
        string source = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SettingsPage.xaml.cs"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("SystemDiagnosticFact", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Facts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clipboard_and_log_paths_stay_behind_typed_desktop_services()
    {
        string page = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SettingsPage.xaml.cs"),
            TestContext.Current.CancellationToken);
        string viewModel = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SystemDiagnosticsViewModel.cs"),
            TestContext.Current.CancellationToken);
        string services = await File.ReadAllTextAsync(
            DesktopSource("Services", "DiagnosticDesktopServices.cs"),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Clipboard", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.", viewModel, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetContent", services,
            StringComparison.Ordinal);
        Assert.Contains("IRegisteredLogFolderService", viewModel,
            StringComparison.Ordinal);
        Assert.DoesNotContain("explorer.exe", viewModel,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Automation_names_and_ids_use_stable_metadata_not_local_facts()
    {
        string source = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SystemDiagnosticsViewModel.cs"),
            TestContext.Current.CancellationToken);

        Assert.Contains("$\"DiagnosticItem-{Id}\"", source,
            StringComparison.Ordinal);
        Assert.Contains("$\"DiagnosticErrorCode-{Id}\"", source,
            StringComparison.Ordinal);
        Assert.Contains(
            "$\"DiagnosticAction-{stableItemId}-{kind}\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("$\"{Title}，{StatusText}\"", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Fact.Value", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalOnly", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_xaml_has_no_arbitrary_command_launch_handlers()
    {
        string xaml = await File.ReadAllTextAsync(
            DesktopSource("Pages", "SettingsPage.xaml"),
            TestContext.Current.CancellationToken);
        int diagnosticsStart = xaml.IndexOf(
            "x:Name=\"SystemDiagnosticsTab\"",
            StringComparison.Ordinal);

        Assert.True(diagnosticsStart >= 0);
        string diagnostics = xaml[diagnosticsStart..];
        Assert.DoesNotContain(" Click=\"", diagnostics,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" Tapped=\"", diagnostics,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" PointerPressed=\"", diagnostics,
            StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ExecuteCommand}\"", diagnostics,
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
}
