using System.Globalization;

namespace TraceFold.Media.Infrastructure.MediaTools;

public sealed class FfmpegProgressParser
{
    private readonly double expectedMicroseconds;
    private int currentPercent;

    public FfmpegProgressParser(TimeSpan expectedDuration)
    {
        if (expectedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDuration));
        }

        expectedMicroseconds = expectedDuration.TotalMicroseconds;
    }

    public int? Accept(string line)
    {
        if (StringComparer.Ordinal.Equals(line, "progress=end"))
        {
            if (currentPercent == 100)
            {
                return null;
            }

            currentPercent = 100;
            return currentPercent;
        }

        const string prefix = "out_time_us=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)
            || !long.TryParse(
                line.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long microseconds)
            || microseconds < 0)
        {
            return null;
        }

        int percent = Math.Clamp(
            (int)Math.Floor(microseconds / expectedMicroseconds * 100d),
            0,
            99);
        if (percent <= currentPercent)
        {
            return null;
        }

        currentPercent = percent;
        return percent;
    }
}
