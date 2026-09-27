using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TraceFold.Media.UiAcceptance.Support;

public enum RootRejection
{
    None,
    NotFound,
    NotDirectTempChild,
    InvalidLeaf,
    ReparsePoint,
    MissingMarker,
    MarkerMismatch,
    UnownedLiveProcess,
}

/// <summary>
/// The file-system operations the ownership rules need. The production implementation uses
/// the operating system; tests can declare reparse points and live process markers that
/// cannot be produced without elevation.
/// </summary>
public interface IRootFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    bool IsReparsePoint(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string contents);

    bool ProcessIsAlive(int processId);

    string[] EnumerateFileSystemEntries(string path);

    void DeleteFile(string path);

    void DeleteDirectory(string path);
}

public sealed class SystemRootFileSystem : IRootFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public bool ProcessIsAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public string[] EnumerateFileSystemEntries(string path) =>
        Directory.GetFileSystemEntries(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path) => Directory.Delete(path);
}

/// <summary>
/// Owns exactly one temporary root: a direct child of the canonical system temp directory
/// whose leaf is <c>TraceFold-UIA-&lt;run id&gt;</c> and whose marker proves the run id and
/// the token. Every path the tool creates, reads or deletes is validated first, and deletion
/// walks the validated tree explicitly instead of using a recursive wildcard.
/// </summary>
public static class OwnedRootManager
{
    public const string MarkerFileName = "owner.marker";

    public const string LiveProcessFileName = "owned-processes.txt";

    private const string LeafPrefix = "TraceFold-UIA-";

    public static string Create(
        string parentDirectory,
        Guid runId,
        string token,
        IRootFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("The run id must not be empty.", nameof(runId));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("The token must not be empty.", nameof(token));
        }

        string parent = CanonicalDirectory(parentDirectory);
        if (!string.Equals(parent, CanonicalDirectory(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The owned root parent must be the canonical system temp directory.");
        }

        string root = Path.Combine(parent, LeafName(runId));
        if (fileSystem.DirectoryExists(root))
        {
            throw new IOException("The owned root already exists.");
        }

        Directory.CreateDirectory(root);
        fileSystem.WriteAllText(
            Path.Combine(root, MarkerFileName),
            MarkerContents(runId, token));
        return root;
    }

    public static RootRejection Validate(
        string root,
        string token,
        IRootFileSystem fileSystem,
        Guid? runId = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
        {
            return RootRejection.NotFound;
        }

        string canonicalRoot = CanonicalDirectory(root);
        string temp = CanonicalDirectory(Path.GetTempPath());
        string? parent = Path.GetDirectoryName(canonicalRoot);
        if (parent is null
            || !string.Equals(parent, temp, StringComparison.OrdinalIgnoreCase))
        {
            return RootRejection.NotDirectTempChild;
        }

        string leaf = Path.GetFileName(canonicalRoot);
        if (!TryParseLeaf(leaf, out Guid leafRunId))
        {
            return RootRejection.InvalidLeaf;
        }

        for (string? current = canonicalRoot;
            current is not null;
            current = Path.GetDirectoryName(current))
        {
            if (fileSystem.IsReparsePoint(current))
            {
                return RootRejection.ReparsePoint;
            }

            if (string.Equals(current, temp, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        string markerPath = Path.Combine(canonicalRoot, MarkerFileName);
        if (!fileSystem.FileExists(markerPath))
        {
            return RootRejection.MissingMarker;
        }

        (string? markerRunId, string? markerTokenHash) =
            ParseMarker(fileSystem.ReadAllText(markerPath));
        if (markerRunId is null || markerTokenHash is null)
        {
            return RootRejection.MarkerMismatch;
        }

        string livePath = Path.Combine(canonicalRoot, LiveProcessFileName);
        bool markerBelongsToAnotherRun = !string.Equals(
            markerRunId,
            runId?.ToString("N") ?? leafRunId.ToString("N"),
            StringComparison.OrdinalIgnoreCase);
        if (markerBelongsToAnotherRun
            && fileSystem.FileExists(livePath)
            && AnyLiveProcess(fileSystem, fileSystem.ReadAllText(livePath)))
        {
            return RootRejection.UnownedLiveProcess;
        }

        if (!string.Equals(markerRunId, leafRunId.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            return RootRejection.MarkerMismatch;
        }

        if (!string.Equals(markerTokenHash, TokenHash(token), StringComparison.OrdinalIgnoreCase))
        {
            return RootRejection.MarkerMismatch;
        }

        if (runId.HasValue && runId.Value != leafRunId)
        {
            return RootRejection.MarkerMismatch;
        }

        return RootRejection.None;
    }

    public static void Delete(
        string root,
        string token,
        IRootFileSystem fileSystem,
        Guid? runId = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        RootRejection rejection = Validate(root, token, fileSystem, runId);
        if (rejection != RootRejection.None)
        {
            throw new IOException(
                "The owned root is not safe to delete: " + rejection + ".");
        }

        string canonicalRoot = CanonicalDirectory(root);
        foreach (string entry in fileSystem.EnumerateFileSystemEntries(canonicalRoot))
        {
            if (fileSystem.DirectoryExists(entry))
            {
                DeleteTree(entry, fileSystem);
                fileSystem.DeleteDirectory(entry);
            }
            else
            {
                fileSystem.DeleteFile(entry);
            }
        }

        fileSystem.DeleteDirectory(canonicalRoot);
    }

    public static string CanonicalDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static string LeafName(Guid runId) => LeafPrefix + runId.ToString("N");

    internal static string TokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string MarkerContents(Guid runId, string token) =>
        "runId=" + runId.ToString("N") + Environment.NewLine
        + "tokenHash=" + TokenHash(token) + Environment.NewLine;

    private static bool TryParseLeaf(string leaf, out Guid runId)
    {
        runId = Guid.Empty;
        if (!leaf.StartsWith(LeafPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string value = leaf[LeafPrefix.Length..];
        return value.Length == 32
            && Guid.TryParseExact(value, "N", out runId);
    }

    private static (string? RunId, string? TokenHash) ParseMarker(string contents)
    {
        string? runId = null;
        string? tokenHash = null;
        foreach (string line in contents.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("runId=", StringComparison.Ordinal))
            {
                runId = trimmed["runId=".Length..];
            }
            else if (trimmed.StartsWith("tokenHash=", StringComparison.Ordinal))
            {
                tokenHash = trimmed["tokenHash=".Length..];
            }
        }

        return (runId, tokenHash);
    }

    private static bool AnyLiveProcess(IRootFileSystem fileSystem, string contents)
    {
        foreach (string line in contents.Split('\n'))
        {
            if (int.TryParse(line.Trim(), out int processId)
                && processId > 0
                && fileSystem.ProcessIsAlive(processId))
            {
                return true;
            }
        }

        return false;
    }

    private static void DeleteTree(string directory, IRootFileSystem fileSystem)
    {
        foreach (string entry in fileSystem.EnumerateFileSystemEntries(directory))
        {
            if (fileSystem.DirectoryExists(entry))
            {
                DeleteTree(entry, fileSystem);
                fileSystem.DeleteDirectory(entry);
            }
            else
            {
                fileSystem.DeleteFile(entry);
            }
        }
    }
}
