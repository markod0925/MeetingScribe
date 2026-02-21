using System.Text;
using MeetingScribe.Core.Export;
using MeetingScribe.Core.Llm;
using MeetingScribe.Core.Models;
using MeetingScribe.Core.Transcript;
using MeetingScribe.Core.Whisper;

namespace MeetingScribe.Core.Pipeline;

public sealed class PipelineOrchestrator(
    WhisperCommandBuilder commandBuilder,
    WhisperRunner whisperRunner,
    WhisperTranscriptParser transcriptParser,
    TranscriptMergeEngine mergeEngine,
    ChunkingService chunking,
    LmStudioClient lmStudioClient,
    SummaryMergeService summaryMerge,
    MarkdownExporter exporter)
{
    public event Action<AppState>? StateChanged;

    public async Task<string> ProcessAsync(string runFolder, string whisperExe, AppSettings settings, RecordingSyncMetadata metadata, CancellationToken ct)
    {
        StateChanged?.Invoke(AppState.TranscribingMic);
        var micCmd = commandBuilder.Build(whisperExe, Path.Combine(runFolder, "mic.wav"), Path.Combine(runFolder, "mic"), settings);
        var micJson = await whisperRunner.RunAsync(micCmd, Path.Combine(runFolder, "mic.json"), _ => { }, ct);

        StateChanged?.Invoke(AppState.TranscribingLoopback);
        var loopCmd = commandBuilder.Build(whisperExe, Path.Combine(runFolder, "loopback.wav"), Path.Combine(runFolder, "loopback"), settings);
        var loopJson = await whisperRunner.RunAsync(loopCmd, Path.Combine(runFolder, "loopback.json"), _ => { }, ct);

        StateChanged?.Invoke(AppState.Merging);
        var micSegs = transcriptParser.Parse(micJson, "You");
        var loopSegs = transcriptParser.Parse(loopJson, "Others");
        var merged = mergeEngine.Merge(micSegs, loopSegs, metadata.InitialOffsetMs);

        StateChanged?.Invoke(AppState.Summarizing);
        var transcriptText = string.Join(Environment.NewLine, merged.Select(s => $"[{s.StartSec:0.00}-{s.EndSec:0.00}] {s.Speaker}: {s.Text}"));
        var chunks = chunking.Split(transcriptText, settings.MaxCharsPerChunk, settings.ChunkOverlapChars);
        var partials = new List<MeetingSummary>();
        foreach (var chunk in chunks)
        {
            var (summary, _) = await lmStudioClient.SummarizeAsync(
                settings.LmStudioBaseUrl,
                settings.LmModel,
                Templates.SystemPrompt,
                Templates.UserPrompt.Replace("{{TITLE}}", "Meeting").Replace("{{DATE_ISO}}", DateTime.UtcNow.ToString("yyyy-MM-dd")).Replace("{{TRANSCRIPT}}", chunk),
                Path.Combine(runFolder, "llm_raw_output.txt"),
                settings.StartupRetryCount,
                settings.StartupRetryDelaySec,
                ct);
            if (summary is not null) partials.Add(summary);
        }
        var finalSummary = partials.Count > 0 ? summaryMerge.Merge(partials) : null;

        StateChanged?.Invoke(AppState.Exporting);
        var outFile = exporter.Export(runFolder, finalSummary, merged);
        StateChanged?.Invoke(AppState.Done);
        return outFile;
    }
}

internal static class Templates
{
    public const string SystemPrompt = "You are a meeting summarization assistant. Return ONLY valid JSON.";
    public const string UserPrompt = "Meeting Title: {{TITLE}}\nDate: {{DATE_ISO}}\n\nTranscript:\n{{TRANSCRIPT}}\n\nReturn JSON only.";
}
