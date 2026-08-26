using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public static class PackageProbeOptions
{
    public const string RootArgument = "--package-probe-root";
    public const string TokenArgument = "--package-probe-token";
    public const string ScenarioArgument = "--package-probe-scenario";
    public const string Stage5ABatchScenario = "stage5a-batch";
    public const string Stage5ARootNamePrefix = "tracefold-media-desktop-stage5a-probe-";
    public const string Stage5AResultFilename = "stage5a-probe-result.json";

    public static PackageProbeOptionResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return new(PackageProbeOptionState.NotRequested, null, null);
        }

        bool hasLegacyArgument = arguments.Any(argument => argument is
            Stage4PackageProbeOptions.RootArgument or
            Stage4PackageProbeOptions.TokenArgument or
            Stage4PackageProbeOptions.ModeArgument or
            Stage4PackageProbeOptions.FaultArgument);
        bool hasGenericArgument = arguments.Any(argument => argument is
            RootArgument or TokenArgument or ScenarioArgument);
        if (hasLegacyArgument && hasGenericArgument)
        {
            return Invalid("probe.invalid_arguments");
        }

        return hasGenericArgument
            ? ParseStage5A(arguments)
            : hasLegacyArgument
                ? ParseStage4(arguments)
                : Invalid("probe.invalid_arguments");
    }

    private static PackageProbeOptionResult ParseStage4(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is not (6 or 8) || arguments.Count % 2 != 0)
        {
            return Invalid("probe.invalid_arguments");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (name is not
                (Stage4PackageProbeOptions.RootArgument or
                 Stage4PackageProbeOptions.TokenArgument or
                 Stage4PackageProbeOptions.ModeArgument or
                 Stage4PackageProbeOptions.FaultArgument)
                || !values.TryAdd(name, arguments[index + 1]))
            {
                return Invalid("probe.invalid_arguments");
            }
        }

        if (!values.TryGetValue(Stage4PackageProbeOptions.RootArgument, out string? rootValue)
            || !values.TryGetValue(Stage4PackageProbeOptions.TokenArgument, out string? tokenText)
            || !values.TryGetValue(Stage4PackageProbeOptions.ModeArgument, out string? mode)
            || string.IsNullOrWhiteSpace(rootValue)
            || string.IsNullOrWhiteSpace(tokenText)
            || !Guid.TryParse(tokenText, out _))
        {
            return Invalid("probe.invalid_arguments");
        }

        bool hasFault = values.TryGetValue(
            Stage4PackageProbeOptions.FaultArgument,
            out string? faultText);
        IngestFaultPoint? faultPoint = null;
        PackageProbeScenarioKind scenario;
        if (StringComparer.Ordinal.Equals(mode, Stage4PackageProbeOptions.CrashSeedMode))
        {
            if (!hasFault || !Enum.TryParse(faultText, false, out IngestFaultPoint parsed)
                || !Enum.IsDefined(parsed))
            {
                return Invalid("probe.invalid_fault");
            }

            scenario = PackageProbeScenarioKind.Stage4CrashSeed;
            faultPoint = parsed;
        }
        else if (StringComparer.Ordinal.Equals(mode, Stage4PackageProbeOptions.CrashResumeMode))
        {
            if (hasFault)
            {
                return Invalid("probe.invalid_arguments");
            }

            scenario = PackageProbeScenarioKind.Stage4CrashResume;
        }
        else if (StringComparer.Ordinal.Equals(mode, Stage4PackageProbeOptions.ConflictMode)
                 && !hasFault)
        {
            scenario = PackageProbeScenarioKind.Stage4Conflict;
        }
        else
        {
            return Invalid("probe.invalid_arguments");
        }

        try
        {
            string root = PackageProbeEnvironment.ValidateRootOwnership(
                rootValue, tokenText, scenario);
            if (!PackageProbeEnvironment.ValidateFixtures(
                    Path.Combine(root, Stage4PackageProbeOptions.FixturesDirectoryName)))
            {
                return Invalid("probe.fixtures_invalid");
            }

            Stage4CrashProbeManifest? manifest = null;
            if (scenario == PackageProbeScenarioKind.Stage4CrashResume)
            {
                if (!PackageProbeEnvironment.ValidateResumeEntries(root))
                {
                    return Invalid("probe.resume_root_invalid");
                }

                manifest = Stage4CrashProbeManifest.ReadValidated(
                    Path.Combine(root, Stage4PackageProbeOptions.CrashManifestFilename),
                    tokenText);
                if (manifest is null)
                {
                    return Invalid("probe.manifest_invalid");
                }

                faultPoint = manifest.FaultPoint;
            }
            else if (!PackageProbeEnvironment.ValidateInitialEntries(root))
            {
                return Invalid("probe.root_not_empty");
            }

            return new(
                PackageProbeOptionState.Ready,
                new(root, tokenText, scenario, faultPoint, manifest),
                null);
        }
        catch (PackageProbeEnvironment.ProbeOptionValidationException error)
        {
            return Invalid(error.ErrorCode);
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or NotSupportedException or UnauthorizedAccessException)
        {
            return Invalid("probe.invalid_root");
        }
    }

    private static PackageProbeOptionResult ParseStage5A(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 6)
        {
            return Invalid("probe.invalid_arguments");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (name is not (RootArgument or TokenArgument or ScenarioArgument)
                || !values.TryAdd(name, arguments[index + 1]))
            {
                return Invalid("probe.invalid_arguments");
            }
        }

        if (!values.TryGetValue(RootArgument, out string? rootValue)
            || !values.TryGetValue(TokenArgument, out string? tokenText)
            || !values.TryGetValue(ScenarioArgument, out string? scenarioText)
            || string.IsNullOrWhiteSpace(rootValue)
            || string.IsNullOrWhiteSpace(tokenText)
            || !Guid.TryParse(tokenText, out _)
            || !StringComparer.Ordinal.Equals(scenarioText, Stage5ABatchScenario))
        {
            return Invalid("probe.invalid_arguments");
        }

        try
        {
            string root = PackageProbeEnvironment.ValidateRootOwnership(
                rootValue, tokenText, PackageProbeScenarioKind.Stage5ABatch);
            if (!PackageProbeEnvironment.ValidateFixtures(
                    Path.Combine(root, Stage4PackageProbeOptions.FixturesDirectoryName)))
            {
                return Invalid("probe.fixtures_invalid");
            }

            if (!PackageProbeEnvironment.ValidateInitialEntries(root))
            {
                return Invalid("probe.root_not_empty");
            }

            return new(
                PackageProbeOptionState.Ready,
                new(root, tokenText, PackageProbeScenarioKind.Stage5ABatch, null, null),
                null);
        }
        catch (PackageProbeEnvironment.ProbeOptionValidationException error)
        {
            return Invalid(error.ErrorCode);
        }
        catch (Exception error) when (error is ArgumentException or IOException
            or NotSupportedException or UnauthorizedAccessException)
        {
            return Invalid("probe.invalid_root");
        }
    }

    private static PackageProbeOptionResult Invalid(string errorCode) =>
        new(PackageProbeOptionState.Invalid, null, errorCode);
}
