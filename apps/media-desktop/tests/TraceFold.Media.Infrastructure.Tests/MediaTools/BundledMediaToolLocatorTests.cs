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
    public void Inspect_distinguishes_a_trusted_missing_ffmpeg()
    {
        using TestDirectory root = TestDirectory.Create();
        File.WriteAllBytes(Path.Combine(root.Path, "ffprobe.exe"), [2]);
        var locator = new BundledMediaToolLocator(new MediaToolOptions(root.Path));

        BundledMediaToolInspection inspection = locator.Inspect();

        Assert.Equal(BundledMediaToolCandidateState.Missing,
            inspection.Ffmpeg.State);
        Assert.Equal(Path.Combine(root.Path, "ffmpeg.exe"),
            inspection.Ffmpeg.Path);
        Assert.Equal(BundledMediaToolCandidateState.Available,
            inspection.Ffprobe.State);
        Assert.Equal(Path.Combine(root.Path, "ffprobe.exe"),
            inspection.Ffprobe.Path);
        Assert.Null(locator.TryResolve());
    }

    [Fact]
    public void Inspect_distinguishes_a_trusted_missing_ffprobe()
    {
        using TestDirectory root = TestDirectory.Create();
        File.WriteAllBytes(Path.Combine(root.Path, "ffmpeg.exe"), [1]);
        var locator = new BundledMediaToolLocator(new MediaToolOptions(root.Path));

        BundledMediaToolInspection inspection = locator.Inspect();

        Assert.Equal(BundledMediaToolCandidateState.Available,
            inspection.Ffmpeg.State);
        Assert.Equal(BundledMediaToolCandidateState.Missing,
            inspection.Ffprobe.State);
        Assert.Equal(Path.Combine(root.Path, "ffprobe.exe"),
            inspection.Ffprobe.Path);
        Assert.Null(locator.TryResolve());
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

    [Fact]
    public void Inspect_exposes_no_candidate_path_for_a_reparse_executable()
    {
        using TestDirectory root = TestDirectory.Create();
        string outside = Path.Combine(root.Path, "outside.exe");
        File.WriteAllBytes(outside, [1]);
        File.CreateSymbolicLink(Path.Combine(root.Path, "ffmpeg.exe"), outside);
        File.WriteAllBytes(Path.Combine(root.Path, "ffprobe.exe"), [2]);

        BundledMediaToolInspection inspection = new BundledMediaToolLocator(
            new MediaToolOptions(root.Path)).Inspect();

        Assert.Equal(BundledMediaToolCandidateState.Unsafe,
            inspection.Ffmpeg.State);
        Assert.Null(inspection.Ffmpeg.Path);
        Assert.Equal(BundledMediaToolCandidateState.Available,
            inspection.Ffprobe.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-tools")]
    public void Inspect_exposes_no_paths_for_an_untrusted_root(string root)
    {
        BundledMediaToolInspection inspection = new BundledMediaToolLocator(
            new MediaToolOptions(root)).Inspect();

        Assert.Equal(BundledMediaToolCandidateState.Unsafe,
            inspection.Ffmpeg.State);
        Assert.Equal(BundledMediaToolCandidateState.Unsafe,
            inspection.Ffprobe.State);
        Assert.Null(inspection.Ffmpeg.Path);
        Assert.Null(inspection.Ffprobe.Path);
    }
}
