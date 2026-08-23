using System.Globalization;
using System.Text.Json;
using TraceFold.Media.Application.Processing;

namespace TraceFold.Media.Infrastructure.MediaTools;

public static class FfprobeJsonParser
{
    public static MediaProbeResult Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement durationElement = root
                .GetProperty("format")
                .GetProperty("duration");
            double durationSeconds = ParseSeconds(durationElement);
            if (!double.IsFinite(durationSeconds)
                || durationSeconds <= 0
                || durationSeconds > TimeSpan.MaxValue.TotalSeconds)
            {
                throw InvalidProbe();
            }

            var streams = new List<MediaStreamInfo>();
            if (root.TryGetProperty("streams", out JsonElement streamElements)
                && streamElements.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement stream in streamElements.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out JsonElement codec)
                        || codec.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? codecType = codec.GetString();
                    if (string.IsNullOrWhiteSpace(codecType))
                    {
                        continue;
                    }

                    TimeSpan? streamDuration = null;
                    if (stream.TryGetProperty("duration", out JsonElement value))
                    {
                        double seconds = ParseSeconds(value);
                        if (double.IsFinite(seconds)
                            && seconds > 0
                            && seconds <= TimeSpan.MaxValue.TotalSeconds)
                        {
                            streamDuration = TimeSpan.FromSeconds(seconds);
                        }
                    }

                    streams.Add(new MediaStreamInfo(
                        codecType.ToLowerInvariant(),
                        streamDuration));
                }
            }

            return new MediaProbeResult(
                TimeSpan.FromSeconds(durationSeconds),
                streams);
        }
        catch (MediaToolException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException
            or KeyNotFoundException
            or InvalidOperationException
            or FormatException
            or OverflowException)
        {
            throw InvalidProbe();
        }
    }

    private static double ParseSeconds(JsonElement element)
    {
        string text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            _ => string.Empty
        };
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static MediaToolException InvalidProbe() =>
        new("processing.probe_invalid", string.Empty, isRetryable: false);
}
