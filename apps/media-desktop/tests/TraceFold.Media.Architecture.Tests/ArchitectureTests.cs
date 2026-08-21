using System.Reflection;
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

    private static string[] ProjectReferences(Assembly assembly) => assembly
        .GetReferencedAssemblies()
        .Select(item => item.Name ?? string.Empty)
        .Where(name => name.StartsWith("TraceFold.Media.", StringComparison.Ordinal))
        .ToArray();
}
