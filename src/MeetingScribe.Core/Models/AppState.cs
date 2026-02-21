namespace MeetingScribe.Core.Models;

public enum AppState
{
    Idle,
    Recording,
    Processing,
    TranscribingMic,
    TranscribingLoopback,
    Merging,
    Summarizing,
    Exporting,
    Cancelling,
    Done,
    Error
}
