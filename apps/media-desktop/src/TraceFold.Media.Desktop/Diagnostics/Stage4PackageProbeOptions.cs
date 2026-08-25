using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public enum Stage4PackageProbeOptionState { NotRequested, Ready, Invalid }

public sealed record Stage4PackageProbeOptionResult(
    Stage4PackageProbeOptionState State,
    Stage4PackageProbeOptions? Options,
    string? ErrorCode);

public sealed record Stage4CrashProbeManifest(
    int Version,
    string OwnerToken,
    IngestFaultPoint FaultPoint,
    Guid AssetId,
    string TargetFilename,
    string OriginalSha256,
    string IncomingSha256,
    Guid? OperationId,
    IngestFaultPoint? ReachedFaultPoint,
    string Integrity)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Stage4CrashProbeManifest Create(
        string ownerToken, IngestFaultPoint faultPoint, Guid assetId,
        string targetFilename, string originalSha256, string incomingSha256)
    {
        var manifest = new Stage4CrashProbeManifest(
            1, ownerToken, faultPoint, assetId, targetFilename, originalSha256,
            incomingSha256, null, null, string.Empty);
        return manifest with { Integrity = manifest.CalculateIntegrity() };
    }

    public static Stage4CrashProbeManifest CreateForTest(
        string ownerToken, IngestFaultPoint faultPoint) =>
        Create(ownerToken, faultPoint,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "registered.jpg", new string('A', 64), new string('B', 64))
        .WithCheckpoint(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), faultPoint);

    public Stage4CrashProbeManifest WithCheckpoint(Guid operationId, IngestFaultPoint reached)
    {
        Stage4CrashProbeManifest changed = this with
        {
            OperationId = operationId,
            ReachedFaultPoint = reached,
            Integrity = string.Empty
        };
        return changed with { Integrity = changed.CalculateIntegrity() };
    }

    public void WriteTo(string path)
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    public static Stage4CrashProbeManifest? ReadValidated(string path, string ownerToken)
    {
        try
        {
            Stage4CrashProbeManifest? manifest = JsonSerializer.Deserialize<Stage4CrashProbeManifest>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            if (manifest is null || manifest.Version != 1
                || !StringComparer.Ordinal.Equals(manifest.OwnerToken, ownerToken)
                || manifest.AssetId == Guid.Empty
                || string.IsNullOrWhiteSpace(manifest.TargetFilename)
                || Path.IsPathRooted(manifest.TargetFilename)
                || !StringComparer.Ordinal.Equals(Path.GetFileName(manifest.TargetFilename), manifest.TargetFilename)
                || !IsSha256(manifest.OriginalSha256) || !IsSha256(manifest.IncomingSha256)
                || !manifest.OperationId.HasValue || manifest.OperationId.Value == Guid.Empty
                || manifest.ReachedFaultPoint != manifest.FaultPoint
                || !StringComparer.Ordinal.Equals(manifest.Integrity, manifest.CalculateIntegrity()))
            {
                return null;
            }

            return manifest;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private string CalculateIntegrity()
    {
        string canonical = string.Join("\n",
            Version.ToString(System.Globalization.CultureInfo.InvariantCulture), OwnerToken,
            FaultPoint.ToString(), AssetId.ToString("D"), TargetFilename, OriginalSha256,
            IncomingSha256, OperationId?.ToString("D") ?? string.Empty,
            ReachedFaultPoint?.ToString() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(character =>
        character is >= '0' and <= '9' or >= 'A' and <= 'F');
}

public sealed record Stage4PackageProbeOptions(
    string Root,
    Guid Token,
    string TokenText,
    string Mode,
    IngestFaultPoint? FaultPoint,
    Stage4CrashProbeManifest? CrashManifest)
{
    public const string RootArgument = "--stage4-package-probe-root";
    public const string TokenArgument = "--stage4-package-probe-token";
    public const string ModeArgument = "--stage4-package-probe-mode";
    public const string FaultArgument = "--stage4-package-probe-fault";
    public const string ConflictMode = "conflict";
    public const string CrashSeedMode = "crash-seed";
    public const string CrashResumeMode = "crash-resume";
    public const string RootNamePrefix = "tracefold-media-desktop-stage4-probe-";
    public const string ResultFilename = "stage4-probe-result.json";
    public const string CrashManifestFilename = "stage4-crash-manifest.json";
    public const string FixturesDirectoryName = "fixtures";
    public const string OwnerFilename = "probe-owner.txt";

    public static Stage4PackageProbeOptionResult Parse(IReadOnlyList<string> arguments)
    {
        PackageProbeOptionResult result = PackageProbeOptions.Parse(arguments);
        if (result.State != PackageProbeOptionState.Ready || result.Request is null)
        {
            return new(
                result.State == PackageProbeOptionState.NotRequested
                    ? Stage4PackageProbeOptionState.NotRequested
                    : Stage4PackageProbeOptionState.Invalid,
                null,
                result.ErrorCode);
        }

        PackageProbeRequest request = result.Request;
        string mode = request.Scenario switch
        {
            PackageProbeScenarioKind.Stage4Conflict => ConflictMode,
            PackageProbeScenarioKind.Stage4CrashSeed => CrashSeedMode,
            PackageProbeScenarioKind.Stage4CrashResume => CrashResumeMode,
            _ => throw new InvalidOperationException("The Stage 4 package probe scenario is not supported.")
        };
        return new(Stage4PackageProbeOptionState.Ready,
            new(request.Root, Guid.Parse(request.OwnerToken), request.OwnerToken, mode,
                request.FaultPoint, request.CrashManifest), null);
    }

    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>
        {
            RootArgument, Root, TokenArgument, TokenText, ModeArgument, Mode
        };
        if (StringComparer.Ordinal.Equals(Mode, CrashSeedMode) && FaultPoint.HasValue)
        {
            arguments.Add(FaultArgument);
            arguments.Add(FaultPoint.Value.ToString());
        }

        return arguments;
    }
}
