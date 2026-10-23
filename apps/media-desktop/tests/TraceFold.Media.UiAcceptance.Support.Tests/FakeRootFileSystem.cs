namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// Deterministic file-system probe for ownership tests. Reparse points and live process
/// markers cannot be produced reliably without elevation, so the tests declare them here
/// while every other operation is delegated to the real file system.
/// </summary>
internal sealed class FakeRootFileSystem : IRootFileSystem
{
    public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<int> LiveProcessIds { get; } = [];

    public string? FailDeletePath { get; set; }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateNewDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException("The root already exists.");
        }

        Directory.CreateDirectory(path);
    }

    public bool IsReparsePoint(string path) =>
        ReparsePoints.Contains(Path.GetFullPath(path));

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public void WriteNewFile(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
    }

    public bool ProcessIsAlive(int processId) => LiveProcessIds.Contains(processId);

    public string[] EnumerateFileSystemEntries(string path) =>
        Directory.GetFileSystemEntries(path);

    public void DeleteFile(string path)
    {
        if (string.Equals(path, FailDeletePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Simulated deletion failure.");
        }

        File.Delete(path);
    }

    public void DeleteDirectory(string path) => Directory.Delete(path);
}
