using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Transcript;

public sealed class TranscriptMergeEngine
{
    public IReadOnlyList<TranscriptSegment> Merge(IReadOnlyList<TranscriptSegment> mic, IReadOnlyList<TranscriptSegment> loopback, double initialOffsetMs)
    {
        var micAdjusted = mic.Select(s => Adjust(s, initialOffsetMs <= 0 ? Math.Abs(initialOffsetMs) / 1000d : 0)).ToList();
        var loopAdjusted = loopback.Select(s => Adjust(s, initialOffsetMs > 0 ? initialOffsetMs / 1000d : 0)).ToList();
        var all = micAdjusted.Concat(loopAdjusted).OrderBy(s => s.StartSec).ToList();

        for (var i = 1; i < all.Count; i++)
        {
            if (all[i].StartSec < all[i - 1].EndSec)
            {
                all[i].IsOverlap = true;
                all[i - 1].IsOverlap = true;
            }
        }

        return all;
    }

    private static TranscriptSegment Adjust(TranscriptSegment s, double shiftSec) => new()
    {
        Speaker = s.Speaker,
        StartSec = Math.Max(0, s.StartSec - shiftSec),
        EndSec = Math.Max(0, s.EndSec - shiftSec),
        Text = s.Text
    };
}
