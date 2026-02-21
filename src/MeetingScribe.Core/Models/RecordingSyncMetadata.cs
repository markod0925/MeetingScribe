namespace MeetingScribe.Core.Models;

public sealed class RecordingSyncMetadata
{
    public required DateTime RecordingStartUtc { get; init; }
    public required long MicFirstSampleTicks { get; init; }
    public required long LoopbackFirstSampleTicks { get; init; }
    public double InitialOffsetMs => (LoopbackFirstSampleTicks - MicFirstSampleTicks) / (double)System.Diagnostics.Stopwatch.Frequency * 1000d;
}
