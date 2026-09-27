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

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsReparsePoint(string path) =>
        ReparsePoints.Contains(Path.GetFullPath(path));

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public bool ProcessIsAlive(int processId) => LiveProcessIds.Contains(processId);

    public string[] EnumerateFileSystemEntries(string path) =>
        Directory.GetFileSystemEntries(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path) => Directory.Delete(path);
}
