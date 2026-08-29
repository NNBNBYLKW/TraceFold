using TraceFold.Media.Application.Ingest;
using TraceFold.Media.Desktop.Diagnostics;
using System.Text.Json;

namespace TraceFold.Media.Desktop.Tests.Diagnostics;

internal sealed class OwnedProbeRoot : IDisposable
{
    private OwnedProbeRoot(string path, Guid token, string tokenText)
    {
        Path = path;
        Token = token;
        TokenText = tokenText;
    }

    public string Path { get; }

    public Guid Token { get; }

    public string TokenText { get; }

    public static OwnedProbeRoot CreateWithFixtures(
        string prefix = "tracefold-media-desktop-stage4-probe-")
    {
        Guid token = Guid.NewGuid();
        string tokenText = token.ToString("D");
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        File.WriteAllText(
            System.IO.Path.Combine(path, "probe-owner.txt"),
            tokenText);
        string fixtures = Directory.CreateDirectory(
            System.IO.Path.Combine(path, "fixtures")).FullName;
        foreach (string filename in new[] { "paired.mp4", "paired.m4a", "photo.jpg" })
        {
            File.Copy(Fixture(filename), System.IO.Path.Combine(fixtures, filename));
        }

        return new OwnedProbeRoot(System.IO.Path.GetFullPath(path), token, tokenText);
    }

    public static OwnedProbeRoot CreateStage5C() =>
        CreateWithFixtures(PackageProbeOptions.Stage5CRootNamePrefix);

    public Stage5CTaxonomyProbeResult ReadStage5CResult()
    {
        string json = File.ReadAllText(System.IO.Path.Combine(
            Path,
            PackageProbeOptions.Stage5CResultFilename));
        return JsonSerializer.Deserialize<Stage5CTaxonomyProbeResult>(json)
            ?? throw new InvalidDataException("The Stage 5C probe result is invalid.");
    }

    public void PrepareCrashResume(IngestFaultPoint point)
    {
        foreach (string name in new[] { "data", "inbox", "library", "work" })
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, name));
        }

        Stage4CrashProbeManifest.CreateForTest(TokenText, point).WriteTo(
            System.IO.Path.Combine(
                Path,
                Stage4PackageProbeOptions.CrashManifestFilename));
    }

    public void Dispose()
    {
        string temp = System.IO.Path.TrimEndingDirectorySeparator(
            System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));
        string? parent = System.IO.Path.GetDirectoryName(Path);
        if (parent is not null
            && StringComparer.OrdinalIgnoreCase.Equals(
                System.IO.Path.TrimEndingDirectorySeparator(parent),
                temp)
            && Directory.Exists(Path))
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 9)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
                }
            }
        }
    }

    private static string Fixture(string filename)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = System.IO.Path.Combine(
                current.FullName,
                "apps",
                "media-api",
                "tests",
                "fixtures",
                "media",
                filename);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository fixture is missing: {filename}");
    }
}

internal static class PackageProbeRequests
{
    public static PackageProbeRequest Stage4Conflict(OwnedProbeRoot root) =>
        new(
            root.Path,
            root.TokenText,
            PackageProbeScenarioKind.Stage4Conflict,
            null,
            null);

    public static PackageProbeRequest Stage5ABatch(OwnedProbeRoot root) =>
        new(
            root.Path,
            root.TokenText,
            PackageProbeScenarioKind.Stage5ABatch,
            null,
            null);

    public static PackageProbeRequest Stage5BLibrary(OwnedProbeRoot root) =>
        new(
            root.Path,
            root.TokenText,
            PackageProbeScenarioKind.Stage5BLibrary,
            null,
            null);

    public static PackageProbeRequest Stage5CTaxonomy(OwnedProbeRoot root) =>
        new(
            root.Path,
            root.TokenText,
            PackageProbeScenarioKind.Stage5CTaxonomy,
            null,
            null);
}
