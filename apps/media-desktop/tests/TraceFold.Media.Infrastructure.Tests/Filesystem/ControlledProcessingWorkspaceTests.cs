using TraceFold.Media.Application.Processing;
using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed class ControlledProcessingWorkspaceTests
{
    [Fact]
    public async Task Prepare_creates_only_the_owned_job_directory()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        Guid jobId = Guid.NewGuid();
        var workspace = new ControlledProcessingWorkspace();

        ProcessingWorkspacePaths paths = await workspace.PrepareAsync(
            jobId,
            folders.Value,
            "paired.mp4",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            Path.Combine(
                folders.Work,
                ".tracefold",
                "processing",
                jobId.ToString("N")),
            paths.RootDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "merged.partial.mp4"),
            paths.PartialOutputPath);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "paired.mp4"),
            paths.FinalOutputPath);
        Assert.True(Directory.Exists(paths.RootDirectory));
    }

    [Theory]
    [InlineData("..\\escape.mp4")]
    [InlineData("sub\\escape.mp4")]
    [InlineData("C:\\escape.mp4")]
    [InlineData("paired.mkv")]
    [InlineData("merged.partial.mp4")]
    public async Task Prepare_rejects_untrusted_output_names(string outputName)
    {
        using TestMediaFolders folders = TestMediaFolders.Create();

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            new ControlledProcessingWorkspace().PrepareAsync(
                Guid.NewGuid(),
                folders.Value,
                outputName,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Promote_moves_the_partial_without_overwriting_the_final()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        var workspace = new ControlledProcessingWorkspace();
        Guid jobId = Guid.NewGuid();
        ProcessingWorkspacePaths paths = await workspace.PrepareAsync(
            jobId,
            folders.Value,
            "paired.mp4",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            paths.PartialOutputPath,
            [1, 2, 3],
            TestContext.Current.CancellationToken);

        string promoted = await workspace.PromoteVerifiedAsync(
            jobId,
            folders.Value,
            paths,
            TestContext.Current.CancellationToken);

        Assert.Equal(paths.FinalOutputPath, promoted);
        Assert.False(File.Exists(paths.PartialOutputPath));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                paths.FinalOutputPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Promote_rejects_a_preexisting_final_and_preserves_both_files()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        var workspace = new ControlledProcessingWorkspace();
        Guid jobId = Guid.NewGuid();
        ProcessingWorkspacePaths paths = await workspace.PrepareAsync(
            jobId,
            folders.Value,
            "paired.mp4",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            paths.PartialOutputPath,
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            paths.FinalOutputPath,
            [2],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() => workspace.PromoteVerifiedAsync(
            jobId,
            folders.Value,
            paths,
            TestContext.Current.CancellationToken));

        Assert.True(File.Exists(paths.PartialOutputPath));
        Assert.Equal(
            [2],
            await File.ReadAllBytesAsync(
                paths.FinalOutputPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cleanup_removes_only_the_exact_job_directory()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        var workspace = new ControlledProcessingWorkspace();
        Guid selected = Guid.NewGuid();
        Guid sibling = Guid.NewGuid();
        ProcessingWorkspacePaths selectedPaths = await workspace.PrepareAsync(
            selected,
            folders.Value,
            "selected.mp4",
            TestContext.Current.CancellationToken);
        ProcessingWorkspacePaths siblingPaths = await workspace.PrepareAsync(
            sibling,
            folders.Value,
            "sibling.mp4",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            selectedPaths.PartialOutputPath,
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            siblingPaths.PartialOutputPath,
            [2],
            TestContext.Current.CancellationToken);

        await workspace.CleanupUnverifiedAsync(
            selected,
            folders.Value,
            TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(selectedPaths.RootDirectory));
        Assert.True(Directory.Exists(siblingPaths.RootDirectory));
    }

    [Fact]
    public async Task Cleanup_rejects_a_reparse_job_directory()
    {
        using TestMediaFolders folders = TestMediaFolders.Create();
        Guid jobId = Guid.NewGuid();
        string outside = Directory.CreateDirectory(
            Path.Combine(folders.Root, "outside")).FullName;
        string processing = Directory.CreateDirectory(Path.Combine(
            folders.Work,
            ".tracefold",
            "processing")).FullName;
        string linked = Path.Combine(processing, jobId.ToString("N"));
        Directory.CreateSymbolicLink(linked, outside);

        await Assert.ThrowsAsync<ControlledPathException>(() =>
            new ControlledProcessingWorkspace().CleanupUnverifiedAsync(
                jobId,
                folders.Value,
                TestContext.Current.CancellationToken));

        Assert.True(Directory.Exists(outside));
    }
}
