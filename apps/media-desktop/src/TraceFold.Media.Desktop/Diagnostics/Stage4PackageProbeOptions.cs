using System.Security.Cryptography;

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

public sealed record Stage4PackageProbeOptions(
    string Root,
    Guid Token,
    string TokenText,
    string Mode)
{
    public const string RootArgument = "--stage4-package-probe-root";
    public const string TokenArgument = "--stage4-package-probe-token";
    public const string ModeArgument = "--stage4-package-probe-mode";
    public const string ConflictMode = "conflict";
    public const string RootNamePrefix =
        "tracefold-media-desktop-stage4-probe-";

    public const string ResultFilename = "stage4-probe-result.json";
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

    private static readonly string[] ReservedEntries =
        ["data", "inbox", "library", "work", ResultFilename];

    public static Stage4PackageProbeOptionResult Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool requested = arguments.Any(argument =>
            argument.Equals(RootArgument, StringComparison.Ordinal)
            || argument.Equals(TokenArgument, StringComparison.Ordinal)
            || argument.Equals(ModeArgument, StringComparison.Ordinal));
        if (!requested)
        {
            return new Stage4PackageProbeOptionResult(
                Stage4PackageProbeOptionState.NotRequested,
                null,
                null);
        }

        if (arguments.Count != 6)
        {
            return Invalid("probe.invalid_arguments");
        }

        string? rootValue = null;
        string? tokenText = null;
        string? mode = null;
        for (int index = 0; index < arguments.Count; index += 2)
        {
            string name = arguments[index];
            string value = arguments[index + 1];
            if (name.Equals(RootArgument, StringComparison.Ordinal)
                && rootValue is null)
            {
                rootValue = value;
            }
            else if (name.Equals(TokenArgument, StringComparison.Ordinal)
                     && tokenText is null)
            {
                tokenText = value;
            }
            else if (name.Equals(ModeArgument, StringComparison.Ordinal)
                     && mode is null)
            {
                mode = value;
            }
            else
            {
                return Invalid("probe.invalid_arguments");
            }
        }

        if (string.IsNullOrWhiteSpace(rootValue)
            || string.IsNullOrWhiteSpace(tokenText)
            || !Guid.TryParse(tokenText, out Guid token)
            || !StringComparer.Ordinal.Equals(mode, ConflictMode))
        {
            return Invalid("probe.invalid_arguments");
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(rootValue));
            string temp = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath()));
            string? parent = Path.GetDirectoryName(root);
            string basename = Path.GetFileName(root);
            if (parent is null
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    Path.TrimEndingDirectorySeparator(parent),
                    temp)
                || !basename.StartsWith(
                    RootNamePrefix,
                    StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(root)
                || IsReparsePoint(root))
            {
                return Invalid("probe.invalid_root");
            }

            string ownerPath = Path.Combine(root, OwnerFilename);
            if (!File.Exists(ownerPath)
                || IsReparsePoint(ownerPath)
                || !StringComparer.Ordinal.Equals(
                    File.ReadAllText(ownerPath),
                    tokenText))
            {
                return Invalid("probe.owner_mismatch");
            }

            if (ReservedEntries.Any(name =>
                    File.Exists(Path.Combine(root, name))
                    || Directory.Exists(Path.Combine(root, name))))
            {
                return Invalid("probe.root_not_empty");
            }

            string[] rootEntries = Directory.GetFileSystemEntries(root)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] expectedRootEntries =
                [FixturesDirectoryName, OwnerFilename];
            Array.Sort(expectedRootEntries, StringComparer.Ordinal);
            if (!rootEntries.SequenceEqual(
                    expectedRootEntries,
                    StringComparer.Ordinal))
            {
                return Invalid("probe.root_not_empty");
            }

            string fixtures = Path.Combine(root, FixturesDirectoryName);
            if (!ValidateFixtures(fixtures))
            {
                return Invalid("probe.fixtures_invalid");
            }

            return new Stage4PackageProbeOptionResult(
                Stage4PackageProbeOptionState.Ready,
                new Stage4PackageProbeOptions(
                    root,
                    token,
                    tokenText,
                    ConflictMode),
                null);
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

    private static Stage4PackageProbeOptionResult Invalid(string errorCode) =>
        new(Stage4PackageProbeOptionState.Invalid, null, errorCode);

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
