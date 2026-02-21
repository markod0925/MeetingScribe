using System.Text.RegularExpressions;

namespace MeetingScribe.Core.Whisper;

public sealed class WhisperProgressParser
{
    private static readonly Regex ProgressRegex = new("(?<pct>\\d{1,3})%", RegexOptions.Compiled);

    public int? TryParsePercentage(string line)
    {
        var m = ProgressRegex.Match(line);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups["pct"].Value, out var pct)) return null;
        return Math.Clamp(pct, 0, 100);
    }
}
