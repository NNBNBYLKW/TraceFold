using TraceFold.Media.Infrastructure.MediaTools;
using Xunit;

namespace TraceFold.Media.Infrastructure.Tests.MediaTools;

public sealed class FfmpegProgressParserTests
{
    [Fact]
    public void Progress_parser_maps_out_time_to_clamped_percent()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));

        Assert.Equal(25, parser.Accept("out_time_us=2500000"));
        Assert.Equal(99, parser.Accept("out_time_us=12000000"));
        Assert.Equal(100, parser.Accept("progress=end"));
    }

    [Fact]
    public void Progress_parser_ignores_malformed_and_decreasing_updates()
    {
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));

        Assert.Null(parser.Accept("out_time_us=invalid"));
        Assert.Equal(50, parser.Accept("out_time_us=5000000"));
        Assert.Null(parser.Accept("out_time_us=2000000"));
        Assert.Null(parser.Accept("unknown=value"));
    }

    [Fact]
    public void Progress_parser_rejects_a_non_positive_expected_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FfmpegProgressParser(TimeSpan.Zero));
    }
}
