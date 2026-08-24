using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceFold.Media.Application.Ingest;

namespace TraceFold.Media.Desktop.Diagnostics;

public enum Stage4PackageProbeOptionState
{
    NotRequested,
    Ready,
    Invalid
}

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
        string ownerToken,
        IngestFaultPoint faultPoint,
        Guid assetId,
        string targetFilename,
        string originalSha256,
        string incomingSha256)
    {
        var manifest = new Stage4CrashProbeManifest(
            1,
            ownerToken,
            faultPoint,
            assetId,
            targetFilename,
            originalSha256,
            incomingSha256,
            null,
            null,
            string.Empty);
        return manifest with { Integrity = manifest.CalculateIntegrity() };
    }

    public static Stage4CrashProbeManifest CreateForTest(
        string ownerToken,
        IngestFaultPoint faultPoint) =>
        Create(
            ownerToken,
            faultPoint,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "registered.jpg",
            new string('A', 64),
            new string('B', 64))
        .WithCheckpoint(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            faultPoint);

    public Stage4CrashProbeManifest WithCheckpoint(
        Guid operationId,
        IngestFaultPoint reached)
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
        using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    public static Stage4CrashProbeManifest? ReadValidated(
        string path,
        string ownerToken)
    {
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            Stage4CrashProbeManifest? manifest =
                JsonSerializer.Deserialize<Stage4CrashProbeManifest>(
                    json,
                    JsonOptions);
            if (manifest is null
                || manifest.Version != 1
                || !StringComparer.Ordinal.Equals(
                    manifest.OwnerToken,
                    ownerToken)
                || manifest.AssetId == Guid.Empty
                || string.IsNullOrWhiteSpace(manifest.TargetFilename)
                || Path.IsPathRooted(manifest.TargetFilename)
                || !StringComparer.Ordinal.Equals(
                    Path.GetFileName(manifest.TargetFilename),
                    manifest.TargetFilename)
                || !IsSha256(manifest.OriginalSha256)
                || !IsSha256(manifest.IncomingSha256)
                || !manifest.OperationId.HasValue
                || manifest.OperationId.Value == Guid.Empty
                || manifest.ReachedFaultPoint != manifest.FaultPoint
                || !StringComparer.Ordinal.Equals(
                    manifest.Integrity,
                    manifest.CalculateIntegrity()))
            {
                return null;
            }

            return manifest;
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            return null;
        }
    }

    private string CalculateIntegrity()
    {
        string canonical = string.Join(
            "\n",
            Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OwnerToken,
            FaultPoint.ToString(),
            AssetId.ToString("D"),
            TargetFilename,
            OriginalSha256,
            IncomingSha256,
            OperationId?.ToString("D") ?? string.Empty,
            ReachedFaultPoint?.ToString() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSha256(string value) =>
        value.Length == 64
        && value.All(character =>
            character is >= '0' and <= '9'
                or >= 'A' and <= 'F');
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
    public const string RootNamePrefix =
        "tracefold-media-desktop-stage4-probe-";

    public const string ResultFilename = "stage4-probe-result.json";
    public const string CrashManifestFilename = "stage4-crash-manifest.json";
    public const string FixturesDirectoryName = "fixtures";
    public const string OwnerFilename = "probe-owner.txt";

    private static readonly IReadOnlyDictionary<string, string> FixtureHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["paired.mp4"] =
                "B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3",
            ["paired.m4a"] =
                "187F3BE0AB2C07C6A967840BEBFA0EB1646227B52179387F74AB4D8BBC64AAC1",
            ["photo.jpg"] =
                "7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6"
        };

    private static readonly string[] RuntimeDirectories =
        ["data", "inbox", "library", "work"];

    public static Stage4PackageProbeOptionResult Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool requested = arguments.Any(argument => argument is
            RootArgument or TokenArgument or ModeArgument or FaultArgument);
        if (!requested)
        {
            return new Stage4PackageProbeOptionResult(
                Stage4PackageProbeOptionState.NotRequested,
                null,
                null);
        }

        if (arguments.Count is not (6 or 8) || arguments.Count % 2 != 0)
        {
            return Invalid("probe.invalid_arguments");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            if (name is not
                (RootArgument or TokenArgument or ModeArgument or FaultArgument)
                || !values.TryAdd(name, arguments[index + 1]))
            {
                return Invalid("probe.invalid_arguments");
            }
        }

        if (!values.TryGetValue(RootArgument, out string? rootValue)
            || !values.TryGetValue(TokenArgument, out string? tokenText)
            || !values.TryGetValue(ModeArgument, out string? mode)
            || string.IsNullOrWhiteSpace(rootValue)
            || string.IsNullOrWhiteSpace(tokenText)
            || !Guid.TryParse(tokenText, out Guid token))
        {
            return Invalid("probe.invalid_arguments");
        }

        bool hasFault = values.TryGetValue(FaultArgument, out string? faultText);
        IngestFaultPoint? faultPoint = null;
        if (StringComparer.Ordinal.Equals(mode, CrashSeedMode))
        {
            if (!hasFault
                || !Enum.TryParse(
                    faultText,
                    ignoreCase: false,
                    out IngestFaultPoint parsed)
                || !Enum.IsDefined(parsed))
            {
                return Invalid("probe.invalid_fault");
            }

            faultPoint = parsed;
        }
        else if (hasFault
                 || mode is not (ConflictMode or CrashResumeMode))
        {
            return Invalid("probe.invalid_arguments");
        }

        try
        {
            string root = ValidateRoot(rootValue, tokenText);
            if (!ValidateFixtures(Path.Combine(root, FixturesDirectoryName)))
            {
                return Invalid("probe.fixtures_invalid");
            }

            Stage4CrashProbeManifest? manifest = null;
            if (StringComparer.Ordinal.Equals(mode, CrashResumeMode))
            {
                if (!ValidateResumeEntries(root))
                {
                    return Invalid("probe.resume_root_invalid");
                }

                string manifestPath = Path.Combine(root, CrashManifestFilename);
                manifest = Stage4CrashProbeManifest.ReadValidated(
                    manifestPath,
                    tokenText);
                if (manifest is null)
                {
                    return Invalid("probe.manifest_invalid");
                }

                faultPoint = manifest.FaultPoint;
            }
            else if (!ValidateInitialEntries(root))
            {
                return Invalid("probe.root_not_empty");
            }

            return new Stage4PackageProbeOptionResult(
                Stage4PackageProbeOptionState.Ready,
                new Stage4PackageProbeOptions(
                    root,
                    token,
                    tokenText,
                    mode,
                    faultPoint,
                    manifest),
                null);
        }
        catch (ProbeOptionValidationException error)
        {
            return Invalid(error.ErrorCode);
        }
        catch (Exception error) when (error is
            ArgumentException or
            IOException or
            NotSupportedException or
            UnauthorizedAccessException)
        {
            return Invalid("probe.invalid_root");
        }
    }

    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>
        {
            RootArgument,
            Root,
            TokenArgument,
            TokenText,
            ModeArgument,
            Mode
        };
        if (StringComparer.Ordinal.Equals(Mode, CrashSeedMode)
            && FaultPoint.HasValue)
        {
            arguments.Add(FaultArgument);
            arguments.Add(FaultPoint.Value.ToString());
        }

        return arguments;
    }

    internal static bool ValidateFixtures(string directory)
    {
        if (!Directory.Exists(directory) || IsReparsePoint(directory))
        {
            return false;
        }

        string[] entries = Directory.GetFileSystemEntries(directory);
        if (entries.Length != FixtureHashes.Count)
        {
            return false;
        }

        foreach ((string filename, string expectedHash) in FixtureHashes)
        {
            string path = Path.Combine(directory, filename);
            if (!File.Exists(path)
                || Directory.Exists(path)
                || IsReparsePoint(path))
            {
                return false;
            }

            using FileStream input = File.OpenRead(path);
            string actualHash = Convert.ToHexString(SHA256.HashData(input));
            if (!StringComparer.Ordinal.Equals(actualHash, expectedHash))
            {
                return false;
            }
        }

        return true;
    }

    private static string ValidateRoot(string rootValue, string tokenText)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootValue));
        string temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        string? parent = Path.GetDirectoryName(root);
        string basename = Path.GetFileName(root);
        if (parent is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(parent),
                temp)
            || !basename.StartsWith(RootNamePrefix, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(root)
            || IsReparsePoint(root))
        {
            throw new IOException("The probe root is not owned.");
        }

        string ownerPath = Path.Combine(root, OwnerFilename);
        if (!File.Exists(ownerPath)
            || IsReparsePoint(ownerPath)
            || !StringComparer.Ordinal.Equals(File.ReadAllText(ownerPath), tokenText))
        {
            throw new ProbeOptionValidationException("probe.owner_mismatch");
        }

        return root;
    }

    private static bool ValidateInitialEntries(string root)
    {
        foreach (string name in RuntimeDirectories.Append(ResultFilename)
                     .Append(CrashManifestFilename))
        {
            if (File.Exists(Path.Combine(root, name))
                || Directory.Exists(Path.Combine(root, name)))
            {
                return false;
            }
        }

        return EntriesEqual(root, [FixturesDirectoryName, OwnerFilename]);
    }

    private static bool ValidateResumeEntries(string root)
    {
        foreach (string name in RuntimeDirectories)
        {
            string path = Path.Combine(root, name);
            if (!Directory.Exists(path) || IsReparsePoint(path))
            {
                return false;
            }
        }

        string manifest = Path.Combine(root, CrashManifestFilename);
        return File.Exists(manifest)
            && !IsReparsePoint(manifest)
            && !File.Exists(Path.Combine(root, ResultFilename))
            && !Directory.Exists(Path.Combine(root, ResultFilename))
            && EntriesEqual(
                root,
                [
                    FixturesDirectoryName,
                    OwnerFilename,
                    CrashManifestFilename,
                    .. RuntimeDirectories
                ]);
    }

    private static bool EntriesEqual(string root, string[] expected)
    {
        string[] actual = Directory.GetFileSystemEntries(root)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Array.Sort(expected, StringComparer.Ordinal);
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static Stage4PackageProbeOptionResult Invalid(string errorCode) =>
        new(Stage4PackageProbeOptionState.Invalid, null, errorCode);

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private sealed class ProbeOptionValidationException(string errorCode)
        : IOException
    {
        public string ErrorCode { get; } = errorCode;
    }
}
