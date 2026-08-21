using TraceFold.Media.Application.Common;

namespace TraceFold.Media.Infrastructure.Paths;

public sealed class LocalAppDataPathProvider(string? rootOverride = null)
    : IAppDataPathProvider
{
    public const string DataRootEnvironmentVariable =
        "TRACEFOLD_MEDIA_DESKTOP_DATA_ROOT";

    public AppDataPaths GetPaths()
    {
        string root = FirstNonBlank(
            rootOverride,
            Environment.GetEnvironmentVariable(DataRootEnvironmentVariable),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TraceFold",
                "MediaDesktop"));

        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException(
                "The media desktop data root must be an absolute path.",
                nameof(rootOverride));
        }

        string canonicalRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root));
        return new AppDataPaths(
            canonicalRoot,
            Path.Combine(canonicalRoot, "media-desktop.db"),
            Path.Combine(canonicalRoot, "logs"),
            Path.Combine(canonicalRoot, "work"));
    }

    private static string FirstNonBlank(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No local application data directory is available.");
    }
}
