using MeetingScribe.Core.Export;
using MeetingScribe.Core.Llm;
using MeetingScribe.Core.Models;
using MeetingScribe.Core.Settings;
using MeetingScribe.Core.Temp;
using MeetingScribe.Core.Templates;
using MeetingScribe.Core.Transcript;
using MeetingScribe.Core.Whisper;

namespace MeetingScribe.Tests;

public class CoreTests
{
    [Fact]
    public void WhisperJsonParser_SupportsStartEndAndT0T1()
    {
        var parser = new WhisperTranscriptParser();
        var json = """
            {"segments":[{"start":1.2,"end":2.3,"text":" hello   world "},{"t0":500,"t1":700,"text":" ciao "}]}
            """;

        var result = parser.Parse(json, "You");

        Assert.Equal(2, result.Count);
        Assert.Equal(5.0, result[1].StartSec);
        Assert.Equal("hello world", result[0].Text);
    }

    [Fact]
    public void MergeOffsetCorrection_ShiftsCorrectTrack()
    {
        var engine = new TranscriptMergeEngine();
        var mic = new[] { new TranscriptSegment { Speaker = "You", StartSec = 1, EndSec = 2, Text = "a" } };
        var loop = new[] { new TranscriptSegment { Speaker = "Others", StartSec = 1.5, EndSec = 2.5, Text = "b" } };

        var merged = engine.Merge(mic, loop, 500);

        Assert.Equal(1.0, merged[0].StartSec);
        Assert.Equal(1.0, merged[1].StartSec);
        Assert.True(merged.Any(s => s.IsOverlap));
    }

    [Fact]
    public void ChunkSplitting_WorksWithOverlap()
    {
        var service = new ChunkingService();
        var text = new string('a', 40);
        var chunks = service.Split(text, 20, 5);
        Assert.Equal(3, chunks.Count);
        Assert.Equal(20, chunks[0].Length);
    }

    [Fact]
    public void ChunkMergeLogic_DedupsAndKeepsHighestPriority()
    {
        var merge = new SummaryMergeService();
        var result = merge.Merge([
            new MeetingSummary { Actions = [new SummaryAction { Text = "Follow up", Priority = "Low", Evidence = "A" }] },
            new MeetingSummary { Actions = [new SummaryAction { Text = " follow   up ", Priority = "High", Evidence = "B" }] }
        ]);

        Assert.Single(result.Actions);
        Assert.Equal("High", result.Actions[0].Priority);
        Assert.Contains("A", result.Actions[0].Evidence);
    }

    [Fact]
    public void MarkdownGeneration_CreatesExpectedFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var exporter = new MarkdownExporter();
        var path = exporter.Export(dir, new MeetingSummary { Title = "Test" }, [new TranscriptSegment { Speaker = "You", StartSec = 0, EndSec = 1, Text = "ciao" }]);

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("## Transcript", content);
    }

    [Fact]
    public void SettingsMigration_UpdatesSchemaVersion()
    {
        var service = new SettingsService();
        var migrated = service.Migrate(new AppSettings { SchemaVersion = 0 });
        Assert.Equal(1, migrated.SchemaVersion);
    }

    [Fact]
    public void TempCleanup_RemovesOldFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingScribe");
        Directory.CreateDirectory(root);
        var old = Path.Combine(root, "old_test_folder");
        Directory.CreateDirectory(old);
        Directory.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-10));

        var svc = new TempFileService();
        svc.CleanupOldRuns(2);

        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void PromptTemplateProvider_FallsBackWhenFilesMissing()
    {
        var provider = new PromptTemplateProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var system = provider.GetSummarySystem();
        var user = provider.GetSummaryUser("Titolo", "2026-01-01", "ciao");

        Assert.Contains("valid JSON", system);
        Assert.Contains("Titolo", user);
    }

    [Fact]
    public void WhisperRunner_ParseCommand_SupportsQuotedExecutablePath()
    {
        var command = "\"C:\\Program Files\\whisper\\whisper-cli.exe\" -m model.bin -f input.wav";

        var parsed = WhisperRunner.ParseCommand(command);

        Assert.Equal("C:\\Program Files\\whisper\\whisper-cli.exe", parsed.FileName);
        Assert.Equal("-m model.bin -f input.wav", parsed.Arguments);
    }

    [Fact]
    public void WhisperRunner_ParseCommand_ThrowsOnUnterminatedQuote()
    {
        var command = "\"C:\\Program Files\\whisper\\whisper-cli.exe -m model.bin";

        var ex = Assert.Throws<ArgumentException>(() => WhisperRunner.ParseCommand(command));

        Assert.Contains("unterminated quote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
