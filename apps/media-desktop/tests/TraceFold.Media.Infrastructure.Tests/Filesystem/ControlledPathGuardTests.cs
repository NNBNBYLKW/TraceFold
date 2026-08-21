using TraceFold.Media.Infrastructure.Filesystem;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.Filesystem;

public sealed class ControlledPathGuardTests
{
    [Fact]
    public void RequireDirectChild_rejects_nested_and_outside_paths()
    {
        using var temp = TestDirectory.Create();
        string root = Directory.CreateDirectory(Path.Combine(temp.Path, "inbox")).FullName;
        string nested = Path.Combine(root, "nested", "photo.jpg");
        string outside = Path.Combine(temp.Path, "outside.jpg");

        Assert.Throws<ControlledPathException>(() =>
            ControlledPathGuard.RequireDirectChild(root, nested));
        Assert.Throws<ControlledPathException>(() =>
            ControlledPathGuard.RequireDirectChild(root, outside));
    }

    [Fact]
    public void RequireDirectChild_accepts_windows_case_differences()
    {
        using var temp = TestDirectory.Create();
        string root = Directory.CreateDirectory(Path.Combine(temp.Path, "Inbox")).FullName;
        string candidate = Path.Combine(root, "Photo.JPG").ToUpperInvariant();

        string result = ControlledPathGuard.RequireDirectChild(
            root.ToLowerInvariant(),
            candidate);

        Assert.True(StringComparer.OrdinalIgnoreCase.Equals(
            Path.GetFullPath(candidate),
            result));
    }

    [Fact]
    public void RequireDescendant_accepts_operation_payload_but_rejects_escape()
    {
        using var temp = TestDirectory.Create();
        string work = Directory.CreateDirectory(Path.Combine(temp.Path, "work")).FullName;
        string payload = Path.Combine(
            work,
            ".tracefold",
            "ingest",
            Guid.NewGuid().ToString("N"),
            "payload");
        string sibling = Path.Combine(temp.Path, "sibling", "payload");
        string escaped = Path.Combine(work, "..", "escaped", "payload");

        Assert.Equal(Path.GetFullPath(payload),
            ControlledPathGuard.RequireDescendant(work, payload));
        Assert.Throws<ControlledPathException>(() =>
            ControlledPathGuard.RequireDescendant(work, sibling));
        Assert.Throws<ControlledPathException>(() =>
            ControlledPathGuard.RequireDescendant(work, escaped));
    }
}
