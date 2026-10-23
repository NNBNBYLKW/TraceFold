using System.Security.Cryptography;
using TraceFold.Media.UiAcceptance.Support;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// Gives each test the canonical system temp directory as the owned-root parent (the
/// ownership rule requires a direct child), a sandbox for paths that must be rejected, and
/// explicit cleanup of every directory the test created.
/// </summary>
internal sealed class TempTree : IDisposable
{
    private readonly List<string> created = [];

    private TempTree(FakeRootFileSystem fileSystem)
    {
        FileSystem = fileSystem;
        TempRoot = OwnedRootManager.CanonicalDirectory(Path.GetTempPath());
        Sandbox = Directory.CreateDirectory(Path.Combine(
            TempRoot,
            "tracefold-uia-support-sandbox-" + Guid.NewGuid().ToString("N"))).FullName;
        RunId = Guid.NewGuid();
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    public string TempRoot { get; }

    public string Sandbox { get; }

    public Guid RunId { get; }

    public string Token { get; }

    public FakeRootFileSystem FileSystem { get; }

    public static TempTree Create() => new(new FakeRootFileSystem());

    public string CreateRoot()
    {
        string root = OwnedRootManager.Create(TempRoot, RunId, Token, FileSystem);
        created.Add(root);
        return root;
    }

    public string CreateLeaf(string leaf)
    {
        string path = Directory.CreateDirectory(Path.Combine(TempRoot, leaf)).FullName;
        created.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in created)
        {
            if (Directory.Exists(path))
            {
                DeleteVerifiedTempChild(path);
            }
        }

        if (Directory.Exists(Sandbox))
        {
            DeleteVerifiedTempChild(Sandbox);
        }
    }

    private void DeleteVerifiedTempChild(string path)
    {
        string canonical = OwnedRootManager.CanonicalDirectory(path);
        if (!string.Equals(Path.GetDirectoryName(canonical), TempRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Test cleanup target escaped the system temp directory.");
        }

        AssertNoReparsePoints(canonical);
        Directory.Delete(canonical, recursive: true);
    }

    private static void AssertNoReparsePoints(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("Test cleanup target contains a reparse point.");
        }

        foreach (string entry in Directory.GetFileSystemEntries(path))
        {
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Test cleanup target contains a reparse point.");
            }

            if (Directory.Exists(entry))
            {
                AssertNoReparsePoints(entry);
            }
        }
    }
}
