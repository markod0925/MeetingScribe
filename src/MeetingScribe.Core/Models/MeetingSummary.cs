namespace MeetingScribe.Core.Models;

public sealed class MeetingSummary
{
    public string Title { get; set; } = "Untitled";
    public string DateIso { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public List<string> SummaryBullets { get; set; } = [];
    public List<string> Decisions { get; set; } = [];
    public List<SummaryAction> Actions { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
    public List<string> Risks { get; set; } = [];
}

public sealed class SummaryAction
{
    public required string Text { get; set; }
    public string? Owner { get; set; }
    public string? DueDateIso { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Evidence { get; set; } = string.Empty;
}
