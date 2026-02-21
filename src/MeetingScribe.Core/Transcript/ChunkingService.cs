namespace MeetingScribe.Core.Transcript;

public sealed class ChunkingService
{
    public IReadOnlyList<string> Split(string transcript, int maxCharsPerChunk, int chunkOverlapChars)
    {
        if (transcript.Length <= maxCharsPerChunk) return [transcript];

        var chunks = new List<string>();
        var start = 0;
        while (start < transcript.Length)
        {
            var len = Math.Min(maxCharsPerChunk, transcript.Length - start);
            chunks.Add(transcript.Substring(start, len));
            if (start + len >= transcript.Length) break;
            start += maxCharsPerChunk - chunkOverlapChars;
        }

        return chunks;
    }
}
