using System.Xml.Linq;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// The product package must never contain or reference the acceptance toolkit. The check
/// reads the project files rather than loaded assemblies so it cannot pass by accident when
/// the toolkit happens to be built in the same output tree.
/// </summary>
public sealed class ToolkitSeparationTests
{
    private const string ToolkitProject =
        "tools/TraceFold.Media.UiAcceptance.Support/TraceFold.Media.UiAcceptance.Support.csproj";

    private static readonly string[] ProductProjects =
    [
        "src/TraceFold.Media.Domain/TraceFold.Media.Domain.csproj",
        "src/TraceFold.Media.Application/TraceFold.Media.Application.csproj",
        "src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj",
        "src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj",
    ];

    [Theory]
    [InlineData("src/TraceFold.Media.Domain/TraceFold.Media.Domain.csproj")]
    [InlineData("src/TraceFold.Media.Application/TraceFold.Media.Application.csproj")]
    [InlineData("src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj")]
    [InlineData("src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj")]
    public void Product_project_does_not_reference_the_acceptance_toolkit(string project)
    {
        string path = Find(project);
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("UiAcceptance.Support", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ui-acceptance", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toolkit_project_is_outside_every_product_project()
    {
        string toolkit = Path.GetFullPath(Find(ToolkitProject));
        foreach (string product in ProductProjects)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(Find(product)))!;
            Assert.False(
                toolkit.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "The toolkit must not live inside a product project.");
        }
    }

    [Fact]
    public void Solution_lists_the_toolkit_in_its_own_folder()
    {
        XDocument solution = XDocument.Load(Find("TraceFold.MediaDesktop.slnx"));
        XElement tools = Assert.Single(solution.Root!.Elements(), element =>
            (string?)element.Attribute("Name") == "/tools/");

        Assert.Contains(tools.Elements(), element =>
            ((string?)element.Attribute("Path"))?
                .Replace('\\', '/')
                .Equals(ToolkitProject, StringComparison.Ordinal) == true);
    }

    private static string Find(string relative)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(
                current.FullName,
                "apps",
                "media-desktop",
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Support test asset is missing: " + relative);
    }
}
