using TraceFold.Media.Application.Intake;
using TraceFold.Media.Domain.Inbox;
using Xunit;

namespace TraceFold.Media.Application.Tests.Intake;

public sealed class CandidateGroupingServiceTests
{
    private static readonly DateTimeOffset Instant =
        DateTimeOffset.Parse("2026-08-22T10:00:00Z");

    [Fact]
    public void Existing_mp4_m4a_pair_is_one_processing_item()
    {
        ObservedFile mp4 = File("C:/inbox/clip.mp4", Instant);
        ObservedFile m4a = File("C:/inbox/CLIP.M4A", Instant);
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(service.Group([mp4, m4a], Instant));

        Assert.Equal(CandidateDisposition.RequiresProcessing, group.Disposition);
        Assert.Equal("mp4_m4a_merge", group.RequiredCapability);
        Assert.Equal(2, group.Sources.Count);
    }

    [Fact]
    public void Lone_mp4_waits_for_companion_for_ten_seconds()
    {
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(
            service.Group(
                [File("C:/inbox/clip.mp4", Instant)],
                Instant.AddSeconds(9)));

        Assert.Equal(CandidateDisposition.WaitingForCompanion, group.Disposition);
    }

    [Fact]
    public void Lone_mp4_becomes_direct_after_companion_window()
    {
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(
            service.Group(
                [File("C:/inbox/clip.mp4", Instant)],
                Instant.AddSeconds(10)));

        Assert.Equal(CandidateDisposition.Direct, group.Disposition);
        Assert.Equal(MediaKind.Video, group.MediaKind);
    }

    [Theory]
    [InlineData("photo.JPEG", MediaKind.Image)]
    [InlineData("movie.WEBM", MediaKind.Video)]
    public void Uppercase_supported_extensions_are_direct(
        string filename,
        MediaKind expectedKind)
    {
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(
            service.Group([File($"C:/inbox/{filename}", Instant)], Instant));

        Assert.Equal(CandidateDisposition.Direct, group.Disposition);
        Assert.Equal(expectedKind, group.MediaKind);
    }

    [Fact]
    public void Temporary_download_suffixes_are_not_observed_extensions()
    {
        CandidateGroupingService service = CreateService();

        Assert.DoesNotContain(".part", service.ObservedExtensions);
        Assert.DoesNotContain(".crdownload", service.ObservedExtensions);
        Assert.DoesNotContain(".tmp", service.ObservedExtensions);
    }

    [Fact]
    public void Lone_m4a_creates_no_item()
    {
        CandidateGroupingService service = CreateService();

        IReadOnlyList<CandidateGroup> groups = service.Group(
            [File("C:/inbox/clip.m4a", Instant)],
            Instant.AddMinutes(1));

        Assert.Empty(groups);
    }

    [Fact]
    public void Different_basenames_are_not_paired()
    {
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(service.Group(
            [
                File("C:/inbox/video.mp4", Instant),
                File("C:/inbox/audio.m4a", Instant)
            ],
            Instant.AddSeconds(5)));

        Assert.Equal(CandidateDisposition.WaitingForCompanion, group.Disposition);
        Assert.Single(group.Sources);
    }

    [Fact]
    public void Pair_is_claimed_before_the_direct_media_rule()
    {
        CandidateGroupingService service = CreateService();

        CandidateGroup group = Assert.Single(service.Group(
            [
                File("C:/inbox/clip.mp4", Instant),
                File("C:/inbox/clip.m4a", Instant)
            ],
            Instant));

        Assert.Equal("mp4_m4a", group.RuleName);
        Assert.Equal(CandidateDisposition.RequiresProcessing, group.Disposition);
    }

    [Fact]
    public void Rule_cannot_claim_the_same_path_twice()
    {
        ObservedFile file = File("C:/inbox/photo.jpg", Instant);
        var service = new CandidateGroupingService([new DuplicateClaimRule(file)]);

        Assert.Throws<InvalidOperationException>(() => service.Group([file], Instant));
    }

    private static CandidateGroupingService CreateService() =>
        new([new DirectMediaGroupingRule(), new Mp4M4aGroupingRule()]);

    private static ObservedFile File(string fullPath, DateTimeOffset firstObservedAtUtc) =>
        new(
            fullPath,
            Path.GetFileName(fullPath),
            Path.GetExtension(fullPath),
            new FileFingerprint(10, firstObservedAtUtc),
            firstObservedAtUtc);

    private sealed class DuplicateClaimRule(ObservedFile file) : IInboxGroupingRule
    {
        public string Name => "invalid";

        public int Priority => 1;

        public IReadOnlySet<string> ObservedExtensions { get; } =
            new HashSet<string>([".jpg"], StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<CandidateGroup> Group(
            IReadOnlyList<ObservedFile> files,
            DateTimeOffset observedAtUtc) =>
            [
                new CandidateGroup(
                    Name,
                    "first",
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [file],
                    null),
                new CandidateGroup(
                    Name,
                    "second",
                    MediaKind.Image,
                    CandidateDisposition.Direct,
                    [file],
                    null)
            ];
    }
}
