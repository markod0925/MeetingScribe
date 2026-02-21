using System.Text;
using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Export;

public sealed class MarkdownExporter
{
    public string Export(string outputDir, MeetingSummary? summary, IReadOnlyList<TranscriptSegment> transcript)
    {
        Directory.CreateDirectory(outputDir);
        var title = summary?.Title ?? "Transcript";
        var date = summary?.DateIso ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var safeTitle = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(outputDir, $"{date} - {safeTitle}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine($"Date: {date}");
        sb.AppendLine();

        if (summary is not null)
        {
            AddList(sb, "Summary", summary.SummaryBullets);
            AddList(sb, "Decisions", summary.Decisions);
            sb.AppendLine("## Actions");
            foreach (var a in summary.Actions)
            {
                sb.AppendLine($"- **{a.Priority}** {a.Text} (Owner: {a.Owner ?? "N/A"}, Due: {a.DueDateIso ?? "N/A"})");
                if (!string.IsNullOrWhiteSpace(a.Evidence)) sb.AppendLine($"  - Evidence: {a.Evidence}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Transcript");
        foreach (var line in transcript)
        {
            sb.AppendLine($"- [{line.StartSec:0.00}-{line.EndSec:0.00}] **{line.Speaker}**: {line.Text}");
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static void AddList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine($"## {title}");
        foreach (var item in items) sb.AppendLine($"- {item}");
        sb.AppendLine();
    }
}
