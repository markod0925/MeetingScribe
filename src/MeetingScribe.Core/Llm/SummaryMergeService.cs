using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Llm;

public sealed class SummaryMergeService
{
    public MeetingSummary Merge(IReadOnlyList<MeetingSummary> partials)
    {
        var merged = new MeetingSummary
        {
            Title = partials.FirstOrDefault()?.Title ?? "Untitled",
            DateIso = partials.FirstOrDefault()?.DateIso ?? DateTime.UtcNow.ToString("yyyy-MM-dd")
        };

        merged.SummaryBullets = partials.SelectMany(x => x.SummaryBullets).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        merged.Decisions = partials.SelectMany(x => x.Decisions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        merged.OpenQuestions = partials.SelectMany(x => x.OpenQuestions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        merged.Risks = partials.SelectMany(x => x.Risks).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        merged.Actions = partials.SelectMany(x => x.Actions)
            .GroupBy(a => Normalize(a.Text), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var top = g.OrderByDescending(a => PriorityRank(a.Priority)).First();
                top.Evidence = string.Join(" | ", g.Select(a => a.Evidence).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct());
                return top;
            }).ToList();

        return merged;
    }

    private static string Normalize(string text) => string.Join(' ', text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int PriorityRank(string priority) => priority.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };
}
