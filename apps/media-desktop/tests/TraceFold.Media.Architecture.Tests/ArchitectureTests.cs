using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TraceFold.Media.Architecture.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_has_no_project_dependencies()
    {
        string[] references = typeof(TraceFold.Media.Domain.DomainAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(item => item.Name ?? string.Empty)
            .Where(name => name.StartsWith("TraceFold.Media.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void Application_references_domain_but_not_infrastructure_or_desktop()
    {
        string[] references = ProjectReferences(
            typeof(TraceFold.Media.Application.ApplicationAssembly).Assembly);

        Assert.Contains("TraceFold.Media.Domain", references);
        Assert.DoesNotContain("TraceFold.Media.Infrastructure", references);
        Assert.DoesNotContain("TraceFold.Media.Desktop", references);
    }

    [Fact]
    public void Infrastructure_never_references_desktop()
    {
        string[] references = ProjectReferences(
            typeof(TraceFold.Media.Infrastructure.InfrastructureAssembly).Assembly);

        Assert.Contains("TraceFold.Media.Application", references);
        Assert.DoesNotContain("TraceFold.Media.Desktop", references);
    }

    [Fact]
    public async Task Desktop_is_the_only_project_referencing_WinUI()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src"));
        string[] projectFiles = Directory.GetFiles(
            sourceRoot,
            "*.csproj",
            SearchOption.AllDirectories);
        Assert.Equal(4, projectFiles.Length);

        foreach (string projectFile in projectFiles)
        {
            string project = await File.ReadAllTextAsync(
                projectFile,
                TestContext.Current.CancellationToken);
            bool referencesWinUi = project.Contains(
                    "<UseWinUI>true</UseWinUI>",
                    StringComparison.Ordinal)
                || project.Contains(
                    "PackageReference Include=\"Microsoft.WindowsAppSDK\"",
                    StringComparison.Ordinal);
            if (Path.GetFileNameWithoutExtension(projectFile)
                == "TraceFold.Media.Desktop")
            {
                Assert.True(referencesWinUi);
            }
            else
            {
                Assert.False(
                    referencesWinUi,
                    $"{projectFile} must not reference WinUI or Windows App SDK.");
            }
        }
    }

    [Fact]
    public async Task ViewModels_do_not_accept_infrastructure_or_process_types()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "TraceFold.Media.Desktop"));
        string[] forbidden =
        [
            "IMediaTool",
            "IProcessingStore",
            "MediaDesktopDbContext",
            "Process"
        ];

        foreach (string viewModel in Directory.GetFiles(
                     sourceRoot,
                     "*ViewModel.cs",
                     SearchOption.AllDirectories))
        {
            string source = await File.ReadAllTextAsync(
                viewModel,
                TestContext.Current.CancellationToken);
            foreach (string typeName in forbidden)
            {
                Assert.False(
                    Regex.IsMatch(
                        source,
                        $@"\b{Regex.Escape(typeName)}\b",
                        RegexOptions.CultureInvariant),
                    $"{viewModel} must not reference {typeName}.");
            }
        }
    }

    private static string[] ProjectReferences(Assembly assembly) => assembly
        .GetReferencedAssemblies()
        .Select(item => item.Name ?? string.Empty)
        .Where(name => name.StartsWith("TraceFold.Media.", StringComparison.Ordinal))
        .ToArray();

}
