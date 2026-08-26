namespace TraceFold.Media.Desktop.Diagnostics;

public static class PackageProbeRunner
{
    public static Task<int> RunAsync(
        PackageProbeRequest request,
        CancellationToken cancellationToken = default) =>
        RunAsync(request, mediaToolRootOverride: null, cancellationToken);

    public static async Task<int> RunAsync(
        PackageProbeRequest request,
        string? mediaToolRootOverride,
        CancellationToken cancellationToken = default)
    {
        PackageProbeEnvironment environment = PackageProbeEnvironment.Create(request, mediaToolRootOverride);
        IPackageProbeScenario scenario = request.Scenario switch
        {
            PackageProbeScenarioKind.Stage4Conflict => new Stage4ConflictProbeScenario(),
            PackageProbeScenarioKind.Stage4CrashSeed or PackageProbeScenarioKind.Stage4CrashResume =>
                new Stage4RecoveryProbeScenario(),
            PackageProbeScenarioKind.Stage5ABatch => new Stage5ABatchProbeScenario(),
            PackageProbeScenarioKind.Stage5BLibrary => new Stage5BLibraryProbeScenario(),
            _ => throw new InvalidOperationException("The package probe scenario is not supported.")
        };
        return await scenario.RunAsync(environment.Request, environment, cancellationToken);
    }
}
