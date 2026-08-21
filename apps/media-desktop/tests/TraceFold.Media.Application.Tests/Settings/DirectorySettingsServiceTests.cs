using TraceFold.Media.Application.Settings;
using Xunit;

namespace TraceFold.Media.Application.Tests.Settings;

public sealed class DirectorySettingsServiceTests
{
    [Fact]
    public async Task ValidateAsync_rejects_nested_directories()
    {
        using var directories = TestDirectories.CreateThree();
        string nestedLibrary = Directory.CreateDirectory(
            Path.Combine(directories.Inbox, "library")).FullName;
        var probe = new FakeDirectoryProbe(
            directories.Inbox,
            nestedLibrary,
            directories.Work);
        var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

        DirectorySettingsResult result = await service.ValidateAsync(
            new LibraryFolders(directories.Inbox, nestedLibrary, directories.Work),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "directory.overlap");
    }

    [Fact]
    public async Task SaveAsync_persists_only_the_canonical_valid_value()
    {
        using var directories = TestDirectories.CreateThree();
        var store = new InMemorySettingsStore();
        var service = new DirectorySettingsService(
            new FakeDirectoryProbe(directories.All),
            store);

        DirectorySettingsResult result = await service.SaveAsync(
            new LibraryFolders(
                Path.Combine(directories.Inbox, "."),
                directories.Library,
                directories.Work),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(directories.Inbox), store.Saved!.InboxDirectory);
        Assert.Equal(store.Saved, result.Folders);
    }

    [Theory]
    [InlineData("", "directory.required")]
    [InlineData("relative-folder", "directory.not_absolute")]
    public async Task ValidateAsync_rejects_missing_or_relative_inbox(
        string inbox,
        string expectedCode)
    {
        using var directories = TestDirectories.CreateThree();
        var service = new DirectorySettingsService(
            new FakeDirectoryProbe(directories.All),
            new InMemorySettingsStore());

        DirectorySettingsResult result = await service.ValidateAsync(
            new LibraryFolders(inbox, directories.Library, directories.Work),
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "inbox" && error.Code == expectedCode);
    }

    [Fact]
    public async Task ValidateAsync_reports_not_found_without_attempting_write_probe()
    {
        using var directories = TestDirectories.CreateThree();
        var probe = new FakeDirectoryProbe(directories.All)
        {
            MissingPath = directories.Inbox
        };
        var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

        DirectorySettingsResult result = await service.ValidateAsync(
            directories.Value,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "inbox" && error.Code == "directory.not_found");
        Assert.DoesNotContain(directories.Inbox, probe.WriteChecks);
    }

    [Fact]
    public async Task ValidateAsync_reports_not_writable()
    {
        using var directories = TestDirectories.CreateThree();
        var probe = new FakeDirectoryProbe(directories.All)
        {
            ReadOnlyPath = directories.Work
        };
        var service = new DirectorySettingsService(probe, new InMemorySettingsStore());

        DirectorySettingsResult result = await service.ValidateAsync(
            directories.Value,
            CancellationToken.None);

        Assert.Contains(result.Errors, error =>
            error.Field == "work" && error.Code == "directory.not_writable");
    }

    [Fact]
    public async Task SaveAsync_does_not_persist_invalid_folders()
    {
        using var directories = TestDirectories.CreateThree();
        var store = new InMemorySettingsStore();
        var service = new DirectorySettingsService(
            new FakeDirectoryProbe(directories.All),
            store);

        DirectorySettingsResult result = await service.SaveAsync(
            new LibraryFolders(
                directories.Inbox,
                directories.Inbox,
                directories.Work),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(store.Saved);
    }

    private sealed class InMemorySettingsStore : IDirectorySettingsStore
    {
        public LibraryFolders? Saved { get; private set; }

        public Task<LibraryFolders?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Saved);

        public Task SaveAsync(
            LibraryFolders folders,
            CancellationToken cancellationToken)
        {
            Saved = folders;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDirectoryProbe(params string[] existing) : IDirectoryProbe
    {
        private readonly HashSet<string> _existing =
            new(existing, StringComparer.OrdinalIgnoreCase);

        public string? MissingPath { get; init; }

        public string? ReadOnlyPath { get; init; }

        public List<string> WriteChecks { get; } = [];

        public ValueTask<bool> ExistsAsync(
            string path,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                !StringComparer.OrdinalIgnoreCase.Equals(path, MissingPath)
                && _existing.Contains(path));

        public ValueTask<bool> CanWriteAsync(
            string path,
            CancellationToken cancellationToken)
        {
            WriteChecks.Add(path);
            return ValueTask.FromResult(
                !StringComparer.OrdinalIgnoreCase.Equals(path, ReadOnlyPath));
        }
    }

    private sealed class TestDirectories : IDisposable
    {
        private TestDirectories(
            string root,
            string inbox,
            string library,
            string work)
        {
            Root = root;
            Inbox = inbox;
            Library = library;
            Work = work;
        }

        public string Root { get; }

        public string Inbox { get; }

        public string Library { get; }

        public string Work { get; }

        public string[] All => [Inbox, Library, Work];

        public LibraryFolders Value => new(Inbox, Library, Work);

        public static TestDirectories CreateThree()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                $"tracefold-settings-{Guid.NewGuid():N}");
            string inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
            string library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
            string work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
            return new TestDirectories(root, inbox, library, work);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
