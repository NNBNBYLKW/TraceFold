namespace TraceFold.Media.Desktop.Diagnostics;

public enum Stage2PackageProbeOptionState
{
    NotRequested,
    Ready,
    Invalid
}

public sealed record Stage2PackageProbeOptionResult(
    Stage2PackageProbeOptionState State,
    Stage2PackageProbeOptions? Options,
    string? ErrorCode);

public sealed record Stage2PackageProbeOptions(
    string Root,
    Guid Token,
    string TokenText)
{
    public const string RootArgument = "--stage2-package-probe-root";
    public const string TokenArgument = "--stage2-package-probe-token";
    public const string RootNamePrefix =
        "tracefold-media-desktop-stage2-probe-";

    public const string ResultFilename = "stage2-probe-result.json";

    private static readonly string[] ReservedEntries =
        ["data", "inbox", "library", "work", ResultFilename];

    public static Stage2PackageProbeOptionResult Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        bool requested = arguments.Any(argument =>
            argument.Equals(RootArgument, StringComparison.Ordinal)
            || argument.Equals(TokenArgument, StringComparison.Ordinal));
        if (!requested)
        {
            return new Stage2PackageProbeOptionResult(
                Stage2PackageProbeOptionState.NotRequested,
                null,
                null);
        }

        if (arguments.Count != 4)
        {
            return Invalid("probe.invalid_arguments");
        }

        string? rootValue = null;
        string? tokenText = null;
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
            else
            {
                return Invalid("probe.invalid_arguments");
            }
        }

        if (string.IsNullOrWhiteSpace(rootValue)
            || string.IsNullOrWhiteSpace(tokenText)
            || !Guid.TryParse(tokenText, out Guid token))
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

            string ownerPath = Path.Combine(root, "probe-owner.txt");
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

            return new Stage2PackageProbeOptionResult(
                Stage2PackageProbeOptionState.Ready,
                new Stage2PackageProbeOptions(root, token, tokenText),
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

    private static Stage2PackageProbeOptionResult Invalid(string errorCode) =>
        new(Stage2PackageProbeOptionState.Invalid, null, errorCode);

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
