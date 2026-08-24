using TraceFold.Media.Application.Settings;

namespace TraceFold.Media.Infrastructure.Tests.Support;

internal sealed class TestMediaFolders : IDisposable
{
    private readonly TestDirectory _root;

    private TestMediaFolders(TestDirectory root)
    {
        _root = root;
        Inbox = Directory.CreateDirectory(Path.Combine(root.Path, "inbox")).FullName;
        Library = Directory.CreateDirectory(Path.Combine(root.Path, "library")).FullName;
        Work = Directory.CreateDirectory(Path.Combine(root.Path, "work")).FullName;
        Value = new LibraryFolders(Inbox, Library, Work);
    }

    public string Root => _root.Path;

    public string Inbox { get; }

    public string Library { get; }

    public string Work { get; }

    public LibraryFolders Value { get; }

    public static TestMediaFolders Create() => new(TestDirectory.Create());

    public string WriteInbox(string filename, byte[] bytes)
    {
        string path = Path.Combine(Inbox, filename);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string WriteLibrary(string filename, byte[] bytes)
    {
        string path = Path.Combine(Library, filename);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose() => _root.Dispose();
}
