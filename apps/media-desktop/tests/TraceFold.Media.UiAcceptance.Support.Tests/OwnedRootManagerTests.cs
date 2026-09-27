using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

public sealed class OwnedRootManagerTests
{
    [Fact]
    public void Accepts_a_direct_child_of_canonical_temp_with_run_id_leaf_and_matching_marker()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();

        Assert.Equal(
            Path.Combine(
                OwnedRootManager.CanonicalDirectory(Path.GetTempPath()),
                "TraceFold-UIA-" + tree.RunId.ToString("N")),
            root);
        Assert.True(tree.FileSystem.FileExists(Path.Combine(root, OwnedRootManager.MarkerFileName)));
        Assert.Equal(RootRejection.None, OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, tree.RunId));
    }

    [Fact]
    public void Create_never_overwrites_an_existing_root()
    {
        using TempTree tree = TempTree.Create();
        _ = tree.CreateRoot();

        Assert.Throws<IOException>(() =>
            OwnedRootManager.Create(tree.TempRoot, tree.RunId, tree.Token, tree.FileSystem));
    }

    [Fact]
    public void Rejects_paths_that_are_not_a_direct_child_of_the_canonical_temp_directory()
    {
        using TempTree tree = TempTree.Create();
        string nested = Directory.CreateDirectory(Path.Combine(
            tree.Sandbox,
            "stage",
            "TraceFold-UIA-" + tree.RunId.ToString("N"))).FullName;

        Assert.Equal(
            RootRejection.NotDirectTempChild,
            OwnedRootManager.Validate(nested, tree.Token, tree.FileSystem, tree.RunId));
        Assert.Equal(
            RootRejection.NotDirectTempChild,
            OwnedRootManager.Validate(tree.TempRoot, tree.Token, tree.FileSystem, tree.RunId));
        Assert.Equal(
            RootRejection.NotDirectTempChild,
            OwnedRootManager.Validate(@"C:\", tree.Token, tree.FileSystem, tree.RunId));
        Assert.Equal(
            RootRejection.NotDirectTempChild,
            OwnedRootManager.Validate(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                tree.Token,
                tree.FileSystem,
                tree.RunId));
    }

    [Theory]
    [InlineData("TraceFold-UIA-not-a-guid")]
    [InlineData("TraceFold-UIA")]
    [InlineData("Unrelated-UIA-00000000000000000000000000000000")]
    public void Rejects_a_leaf_that_is_not_a_run_scoped_owned_name(string leaf)
    {
        using TempTree tree = TempTree.Create();
        string path = tree.CreateLeaf(leaf);

        Assert.Equal(
            RootRejection.InvalidLeaf,
            OwnedRootManager.Validate(path, tree.Token, tree.FileSystem, tree.RunId));
    }

    [Fact]
    public void Rejects_a_missing_or_mismatched_marker()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();

        Assert.Equal(
            RootRejection.MarkerMismatch,
            OwnedRootManager.Validate(root, "another-token", tree.FileSystem, tree.RunId));

        tree.FileSystem.DeleteFile(Path.Combine(root, OwnedRootManager.MarkerFileName));
        Assert.Equal(
            RootRejection.MissingMarker,
            OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, tree.RunId));
    }

    [Fact]
    public void Rejects_a_run_id_that_does_not_match_the_marker()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();

        Assert.Equal(
            RootRejection.MarkerMismatch,
            OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, Guid.NewGuid()));
    }

    [Fact]
    public void Rejects_a_root_below_a_reparse_point()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        tree.FileSystem.ReparsePoints.Add(OwnedRootManager.CanonicalDirectory(Path.GetTempPath()));
        tree.FileSystem.ReparsePoints.Add(root);

        Assert.Equal(
            RootRejection.ReparsePoint,
            OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, tree.RunId));
        Assert.Throws<IOException>(() =>
            OwnedRootManager.Delete(root, tree.Token, tree.FileSystem, tree.RunId));
    }

    [Fact]
    public void Rejects_a_root_holding_an_unowned_live_process_marker()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        tree.FileSystem.WriteAllText(
            Path.Combine(root, OwnedRootManager.LiveProcessFileName),
            "4242");
        tree.FileSystem.LiveProcessIds.Add(4242);

        Assert.Equal(
            RootRejection.UnownedLiveProcess,
            OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, Guid.NewGuid()));
        Assert.Throws<IOException>(() =>
            OwnedRootManager.Delete(root, tree.Token, tree.FileSystem, Guid.NewGuid()));
    }

    [Fact]
    public void Delete_removes_only_the_validated_root()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();
        string child = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        File.WriteAllText(Path.Combine(child, "media-desktop.db"), "seed");
        string sibling = tree.CreateLeaf("TraceFold-UIA-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(Path.Combine(sibling, "keep.txt"), "keep");

        OwnedRootManager.Delete(root, tree.Token, tree.FileSystem, tree.RunId);

        Assert.False(tree.FileSystem.DirectoryExists(root));
        Assert.True(tree.FileSystem.DirectoryExists(sibling));
        Assert.Equal(
            RootRejection.NotFound,
            OwnedRootManager.Validate(root, tree.Token, tree.FileSystem, tree.RunId));
    }

    [Fact]
    public void Delete_rejects_a_root_it_cannot_validate()
    {
        using TempTree tree = TempTree.Create();
        string root = tree.CreateRoot();

        Assert.Throws<IOException>(() =>
            OwnedRootManager.Delete(root, "wrong-token", tree.FileSystem, tree.RunId));
        Assert.True(tree.FileSystem.DirectoryExists(root));
        Assert.Equal(
            RootRejection.NotDirectTempChild,
            OwnedRootManager.Validate(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                tree.Token,
                tree.FileSystem,
                tree.RunId));
    }
}
