using MeetingScribe.Core.Export;
using MeetingScribe.Core.Llm;
using MeetingScribe.Core.Models;
using MeetingScribe.Core.Templates;
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
    MarkdownExporter exporter,
    PromptTemplateProvider templates)
{
    public event Action<AppState>? StateChanged;

    public async Task<string> ProcessAsync(string runFolder, string whisperExe, AppSettings settings, RecordingSyncMetadata metadata, CancellationToken ct)
    {
        StateChanged?.Invoke(AppState.Processing);

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
        var rawOutPath = Path.Combine(runFolder, "llm_raw_output.txt");
        var systemPrompt = templates.GetSummarySystem();
        var repairPrompt = templates.GetRepairSystem();

        foreach (var chunk in chunks)
        {
            var userPrompt = templates.GetSummaryUser("Meeting", DateTime.UtcNow.ToString("yyyy-MM-dd"), chunk);
            var (summary, _) = await lmStudioClient.SummarizeAsync(
                settings.LmStudioBaseUrl,
                settings.LmModel,
                systemPrompt,
                userPrompt,
                repairPrompt,
                rawOutPath,
                settings.StartupRetryCount,
                settings.StartupRetryDelaySec,
                ct);
            if (summary is not null)
            {
                partials.Add(summary);
            }
        }

        var finalSummary = partials.Count > 0 ? summaryMerge.Merge(partials) : null;

        StateChanged?.Invoke(AppState.Exporting);
        var rawAppendix = File.Exists(rawOutPath) ? await File.ReadAllTextAsync(rawOutPath, ct) : null;
        var outFile = exporter.Export(runFolder, finalSummary, merged, rawAppendix);

        StateChanged?.Invoke(AppState.Done);
        return outFile;
    }
}
