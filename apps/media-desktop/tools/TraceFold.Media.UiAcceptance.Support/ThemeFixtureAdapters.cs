using System.Security;
using System.Text.Json;

namespace TraceFold.Media.UiAcceptance.Support;

/// <summary>
/// Acceptance-only adapter injection for the theme command.
/// </summary>
/// <remarks>
/// These adapters are selected exclusively by <c>--fixture-root</c>, which the command accepts
/// only for a licensed process and only inside a <c>%TEMP%\tracefold-theme-fixture-*</c> root
/// whose journal lives directly in that root. The journal is still the real file store; only the
/// registry, the change broadcaster and the owner probe are replaced. They never read or write a
/// real registry value, never broadcast and never inspect a real process, so a fixture run cannot
/// change the machine's theme.
/// </remarks>
internal static class ThemeFixtureAdapters
{
    public const string LicenseVariable = "TRACEFOLD_UIACCEPTANCE_ALLOW_THEME_FIXTURE";

    private const string RegistryFileName = "registry.json";
    private const string BroadcastFileName = "broadcast.json";
    private const string ProbeFileName = "probe.json";

    public static bool Licensed => string.Equals(
        Environment.GetEnvironmentVariable(LicenseVariable),
        "1",
        StringComparison.Ordinal);

    /// <summary>
    /// Creates the three fixture adapters. Every leaf is checked before an adapter object exists,
    /// so no journal read, adapter call or write can happen through an unchecked path.
    /// </summary>
    public static (IThemeRegistry, IThemeChangeBroadcaster, IThemeOwnerInspector) Create(string root)
    {
        string normalized = NormalizeRoot(root);
        ValidateLeaf(normalized, RegistryFileName);
        ValidateLeaf(normalized, BroadcastFileName);
        ValidateLeaf(normalized, ProbeFileName);
        return (new FixtureRegistry(Path.Combine(normalized, RegistryFileName)),
            new FixtureBroadcaster(Path.Combine(normalized, BroadcastFileName)),
            new FixtureInspector(Path.Combine(normalized, ProbeFileName)));
    }

    public static IThemeOwnerInspector CreateInspector(string root)
    {
        string normalized = NormalizeRoot(root);
        // The three files are one fixture, not three independent ones: a partial fixture is not a
        // fixture. Checking all of them here also keeps inspect from being the one command that
        // could read a journal while a sibling leaf is redirected.
        ValidateLeaf(normalized, RegistryFileName);
        ValidateLeaf(normalized, BroadcastFileName);
        ValidateLeaf(normalized, ProbeFileName);
        return new FixtureInspector(Path.Combine(normalized, ProbeFileName));
    }

    /// <summary>
    /// The root is normalised here too, but only as a path. Whether it is under the temporary
    /// directory, carries the acceptance prefix, owns the journal and sits on a link-free parent
    /// chain is decided by the caller before the adapters are created; this method does not
    /// restate that rule and does not treat its own checks as the whole chain's checks.
    /// </summary>
    private static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ThemeFixtureException("The theme fixture root is invalid.");
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            throw new ThemeFixtureException("The theme fixture root is invalid.");
        }

        if (!Directory.Exists(normalized))
        {
            throw new ThemeFixtureException("The theme fixture root is missing.");
        }

        if (IsReparsePoint(normalized))
        {
            throw new ThemeFixtureException("The theme fixture root is a reparse point.");
        }

        return normalized;
    }

    /// <summary>
    /// A leaf is valid only when it exists as a plain file directly inside the root. A missing or
    /// unreadable file is refused, never replaced by an empty state: "the fixture did not say" and
    /// "the fixture said absent" are different facts and only the second one may drive a restore.
    /// </summary>
    private static void ValidateLeaf(string root, string name)
    {
        string path = Path.Combine(root, name);
        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            throw new ThemeFixtureException("The theme fixture file is invalid.");
        }

        // The names are constants, so this can only fail if the root itself was rewritten; the
        // comparison is still made rather than assumed.
        if (!string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ThemeFixtureException("The theme fixture file is outside its root.");
        }

        FileAttributes attributes;
        try
        {
            if (!File.Exists(path))
            {
                throw new ThemeFixtureException("The theme fixture file is missing.");
            }

            attributes = File.GetAttributes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or SecurityException)
        {
            throw new ThemeFixtureException("The theme fixture file is unreadable.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ThemeFixtureException("The theme fixture file is a reparse point.");
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            throw new ThemeFixtureException("The theme fixture file is not a file.");
        }

        // A leaf can also be redirected through a link this process cannot see in the attributes
        // (for example a hard link to a path outside the root). Resolving the final target keeps
        // the check about the real file, not about the name that was written.
        try
        {
            FileSystemInfo? target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                throw new ThemeFixtureException("The theme fixture file resolves through a link.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ThemeFixtureException("The theme fixture file is unreadable.");
        }

        // Readability is proven now, before any journal or adapter operation. The handle is
        // opened read-only with sharing so a fixture rewrite is not blocked by this probe.
        try
        {
            using FileStream probe = new(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            _ = probe.Length;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or SecurityException)
        {
            throw new ThemeFixtureException("The theme fixture file is unreadable.");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or SecurityException)
        {
            return true;
        }
    }

    private static JsonDocument ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The theme fixture file is missing.");
        }

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void WriteDocument(string path, Dictionary<string, object?> document) =>
        File.WriteAllText(path, JsonSerializer.Serialize(document));

    private sealed class FixtureRegistry : IThemeRegistry
    {
        private readonly string path;

        public FixtureRegistry(string path) => this.path = path;

        public ThemeRegistrySnapshot? Read(string keyPath, string valueName)
        {
            using JsonDocument document = ReadDocument(path);
            JsonElement root = document.RootElement;
            if (!root.GetProperty("present").GetBoolean())
            {
                return new ThemeRegistrySnapshot(false, ThemeValueKind.DWord, null);
            }

            ThemeValueKind kind = Enum.Parse<ThemeValueKind>(
                root.GetProperty("kind").GetString()!, ignoreCase: true);
            byte[] bytes = Convert.FromHexString(root.GetProperty("bytesHex").GetString()!);
            return new ThemeRegistrySnapshot(true, kind, bytes);
        }

        public bool Write(string keyPath, string valueName, ThemeValueKind kind, byte[] bytes)
        {
            using JsonDocument document = ReadDocument(path);
            JsonElement root = document.RootElement;
            if (root.GetProperty("failWrite").GetBoolean())
            {
                return false;
            }

            WriteDocument(path, new Dictionary<string, object?>
            {
                ["present"] = true,
                ["kind"] = kind.ToString(),
                ["bytesHex"] = Convert.ToHexString(bytes),
                ["failWrite"] = false,
                ["failDelete"] = root.TryGetProperty("failDelete", out JsonElement failDelete)
                    && failDelete.GetBoolean(),
            });
            return true;
        }

        public bool Delete(string keyPath, string valueName)
        {
            using JsonDocument document = ReadDocument(path);
            JsonElement root = document.RootElement;
            if (root.GetProperty("failDelete").GetBoolean())
            {
                return false;
            }

            WriteDocument(path, new Dictionary<string, object?>
            {
                ["present"] = false,
                ["kind"] = root.GetProperty("kind").GetString(),
                ["bytesHex"] = root.GetProperty("bytesHex").GetString(),
                ["failWrite"] = root.TryGetProperty("failWrite", out JsonElement failWrite)
                    && failWrite.GetBoolean(),
                ["failDelete"] = false,
            });
            return true;
        }
    }

    private sealed class FixtureBroadcaster : IThemeChangeBroadcaster
    {
        private readonly string path;

        public FixtureBroadcaster(string path) => this.path = path;

        public bool Broadcast()
        {
            using JsonDocument document = ReadDocument(path);
            JsonElement root = document.RootElement;
            if (root.GetProperty("fail").GetBoolean())
            {
                return false;
            }

            int count = root.GetProperty("count").GetInt32();
            WriteDocument(path, new Dictionary<string, object?>
            {
                ["fail"] = false,
                ["count"] = count + 1,
            });
            return true;
        }
    }

    private sealed class FixtureInspector : IThemeOwnerInspector
    {
        private readonly string path;

        public FixtureInspector(string path) => this.path = path;

        public ThemeOwnerProbe Probe(ThemeOwnerIdentity owner)
        {
            using JsonDocument document = ReadDocument(path);
            JsonElement root = document.RootElement;
            ThemeOwnerState state = root.GetProperty("state").GetString() switch
            {
                "live" => ThemeOwnerState.Live,
                "dead" => ThemeOwnerState.Dead,
                "pid-reused" => ThemeOwnerState.PidReused,
                _ => ThemeOwnerState.Unknown,
            };
            return new ThemeOwnerProbe(
                state,
                root.GetProperty("identityReadable").GetBoolean(),
                root.TryGetProperty("detail", out JsonElement detail)
                    ? detail.GetString() ?? string.Empty
                    : string.Empty);
        }
    }
}

/// <summary>
/// A refusal raised while creating the acceptance fixture adapters. The message is a fixed,
/// path-free sentence: it becomes the command's JSON error body and must never echo a path or a
/// capability value.
/// </summary>
internal sealed class ThemeFixtureException : Exception
{
    public ThemeFixtureException(string message)
        : base(message)
    {
    }
}
