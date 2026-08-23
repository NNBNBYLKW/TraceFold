using TraceFold.Media.Infrastructure.MediaTools;
using TraceFold.Media.Infrastructure.Tests.Support;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.MediaTools;

public sealed class BundledMediaToolLocatorTests
{
    [Fact]
    public void Locator_resolves_only_both_executables_under_the_explicit_root()
    {
        using TestDirectory root = TestDirectory.Create();
        File.WriteAllBytes(Path.Combine(root.Path, "ffmpeg.exe"), [1]);
        File.WriteAllBytes(Path.Combine(root.Path, "ffprobe.exe"), [2]);
        var locator = new BundledMediaToolLocator(new MediaToolOptions(root.Path));

        MediaToolLocation? location = locator.TryResolve();

        Assert.Equal(Path.Combine(root.Path, "ffmpeg.exe"), location!.FfmpegPath);
        Assert.Equal(Path.Combine(root.Path, "ffprobe.exe"), location.FfprobePath);
    }

    [Fact]
    public void Locator_never_falls_back_to_path()
    {
        using TestDirectory root = TestDirectory.Create();
        var locator = new BundledMediaToolLocator(new MediaToolOptions(root.Path));

        MediaToolLocation? location = locator.TryResolve();

        Assert.Null(location);
    }

    [Fact]
    public void Locator_rejects_a_reparse_executable()
    {
        using TestDirectory root = TestDirectory.Create();
        string outside = Path.Combine(root.Path, "outside.exe");
        File.WriteAllBytes(outside, [1]);
        File.CreateSymbolicLink(Path.Combine(root.Path, "ffmpeg.exe"), outside);
        File.WriteAllBytes(Path.Combine(root.Path, "ffprobe.exe"), [2]);

        MediaToolLocation? location = new BundledMediaToolLocator(
            new MediaToolOptions(root.Path)).TryResolve();

        Assert.Null(location);
    }
}
