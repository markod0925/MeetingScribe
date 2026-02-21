namespace MeetingScribe.Core.Models;

public sealed class TranscriptSegment
{
    public required string Speaker { get; init; }
    public double StartSec { get; init; }
    public double EndSec { get; init; }
    public required string Text { get; init; }
    public bool IsOverlap { get; set; }
}
