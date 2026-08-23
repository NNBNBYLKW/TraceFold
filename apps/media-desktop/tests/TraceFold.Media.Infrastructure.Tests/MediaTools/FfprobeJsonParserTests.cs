using TraceFold.Media.Application.Processing;
using TraceFold.Media.Infrastructure.MediaTools;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.MediaTools;

public sealed class FfprobeJsonParserTests
{
    [Fact]
    public void Probe_parser_requires_positive_duration_and_exposes_stream_types()
    {
        const string json = """
            {"streams":[{"codec_type":"VIDEO"},{"codec_type":"audio"}],
             "format":{"duration":"2.500000"}}
            """;

        MediaProbeResult result = FfprobeJsonParser.Parse(json);

        Assert.Equal(TimeSpan.FromSeconds(2.5), result.Duration);
        Assert.True(result.HasVideo);
        Assert.True(result.HasAudio);
        Assert.Equal(["video", "audio"],
            result.Streams.Select(stream => stream.CodecType));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    public void Probe_parser_rejects_non_positive_or_non_finite_duration(
        string duration)
    {
        string json =
            "{\"streams\":[],\"format\":{\"duration\":\""
            + duration
            + "\"}}";

        Assert.Throws<MediaToolException>(() => FfprobeJsonParser.Parse(json));
    }

    [Fact]
    public void Probe_parser_rejects_malformed_json_with_a_stable_error()
    {
        MediaToolException error = Assert.Throws<MediaToolException>(() =>
            FfprobeJsonParser.Parse("not-json"));

        Assert.Equal("processing.probe_invalid", error.ErrorCode);
    }
}
