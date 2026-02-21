using System.Text.Json;
using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Transcript;

public sealed class WhisperTranscriptParser
{
    public IReadOnlyList<TranscriptSegment> Parse(string json, string speaker)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var output = new List<TranscriptSegment>();
        foreach (var segment in segments.EnumerateArray())
        {
            var start = GetStart(segment);
            var end = GetEnd(segment);
            var text = Normalize(segment.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty);
            output.Add(new TranscriptSegment
            {
                Speaker = speaker,
                StartSec = start,
                EndSec = end,
                Text = text
            });
        }

        return output;
    }

    private static double GetStart(JsonElement segment)
    {
        if (segment.TryGetProperty("start", out var start)) return start.GetDouble();
        if (segment.TryGetProperty("t0", out var t0)) return t0.GetDouble() / 100d;
        return 0;
    }

    private static double GetEnd(JsonElement segment)
    {
        if (segment.TryGetProperty("end", out var end)) return end.GetDouble();
        if (segment.TryGetProperty("t1", out var t1)) return t1.GetDouble() / 100d;
        return 0;
    }

    private static string Normalize(string text) => string.Join(' ', text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
