namespace TraceFold.Media.Infrastructure.Tests.Support;

internal sealed class TestDirectory : IDisposable
{
    private TestDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TestDirectory Create()
    {
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"tracefold-media-desktop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
