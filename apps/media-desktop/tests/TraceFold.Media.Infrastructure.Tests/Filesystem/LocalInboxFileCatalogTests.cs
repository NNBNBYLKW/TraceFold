using TraceFold.Media.Application.Intake;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed class LocalInboxFileCatalogTests
{
    [Fact]
    public async Task EnumerateTopLevelAsync_returns_only_supported_direct_children()
    {
        using var temp = TestDirectory.Create();
        string inbox = Directory.CreateDirectory(Path.Combine(temp.Path, "inbox")).FullName;
        File.WriteAllBytes(Path.Combine(inbox, "photo.JPG"), [1]);
        File.WriteAllBytes(Path.Combine(inbox, "clip.mp4.part"), [2]);
        File.WriteAllBytes(Path.Combine(inbox, "notes.txt"), [3]);
        string nested = Directory.CreateDirectory(Path.Combine(inbox, "nested")).FullName;
        File.WriteAllBytes(Path.Combine(nested, "hidden.png"), [4]);
        var catalog = new LocalInboxFileCatalog();

        IReadOnlyList<FileSnapshot> result = await catalog.EnumerateTopLevelAsync(
            inbox,
            new HashSet<string>([".jpg", ".png", ".mp4"], StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        FileSnapshot file = Assert.Single(result);
        Assert.Equal("photo.JPG", file.Filename);
        Assert.Equal(1, file.Fingerprint.Size);
    }

    [Fact]
    public async Task EnumerateTopLevelAsync_orders_names_case_insensitively()
    {
        using var temp = TestDirectory.Create();
        string inbox = Directory.CreateDirectory(Path.Combine(temp.Path, "inbox")).FullName;
        File.WriteAllBytes(Path.Combine(inbox, "zebra.png"), [1]);
        File.WriteAllBytes(Path.Combine(inbox, "Alpha.jpg"), [2]);
        var catalog = new LocalInboxFileCatalog();

        IReadOnlyList<FileSnapshot> result = await catalog.EnumerateTopLevelAsync(
            inbox,
            new HashSet<string>([".jpg", ".png"], StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Equal(["Alpha.jpg", "zebra.png"], result.Select(file => file.Filename));
    }

    [Fact]
    public async Task EnumerateTopLevelAsync_skips_file_reparse_points()
    {
        using var temp = TestDirectory.Create();
        string inbox = Directory.CreateDirectory(Path.Combine(temp.Path, "inbox")).FullName;
        string target = Path.Combine(temp.Path, "outside.jpg");
        File.WriteAllBytes(target, [1, 2, 3]);
        File.CreateSymbolicLink(Path.Combine(inbox, "linked.jpg"), target);
        var catalog = new LocalInboxFileCatalog();

        IReadOnlyList<FileSnapshot> result = await catalog.EnumerateTopLevelAsync(
            inbox,
            new HashSet<string>([".jpg"], StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EnumerateTopLevelAsync_reports_a_stable_missing_root_error()
    {
        using var temp = TestDirectory.Create();
        string missing = Path.Combine(temp.Path, "missing");
        var catalog = new LocalInboxFileCatalog();

        IntakeFileAccessException error = await Assert.ThrowsAsync<IntakeFileAccessException>(
            () => catalog.EnumerateTopLevelAsync(
                missing,
                new HashSet<string>([".jpg"], StringComparer.OrdinalIgnoreCase),
                CancellationToken.None));

        Assert.Equal("intake.directory_not_found", error.ErrorCode);
        Assert.Equal(Path.GetFullPath(missing), error.Root);
    }

    [Fact]
    public async Task EnumerateTopLevelAsync_observes_preexisting_cancellation()
    {
        using var temp = TestDirectory.Create();
        var catalog = new LocalInboxFileCatalog();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => catalog.EnumerateTopLevelAsync(
                temp.Path,
                new HashSet<string>([".jpg"], StringComparer.OrdinalIgnoreCase),
                cancellation.Token));
    }
}
