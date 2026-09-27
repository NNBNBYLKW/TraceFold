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
                Directory.Delete(path, recursive: true);
            }
        }

        if (Directory.Exists(Sandbox))
        {
            Directory.Delete(Sandbox, recursive: true);
        }
    }
}
