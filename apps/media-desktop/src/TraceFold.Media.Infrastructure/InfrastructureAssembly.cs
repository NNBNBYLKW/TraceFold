namespace TraceFold.Media.Infrastructure;

public static class InfrastructureAssembly
{
    public static System.Reflection.Assembly Application =>
        typeof(TraceFold.Media.Application.ApplicationAssembly).Assembly;

    public static System.Reflection.Assembly Domain =>
        typeof(TraceFold.Media.Domain.DomainAssembly).Assembly;
}
