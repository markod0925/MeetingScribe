namespace MeetingScribe.Core.Templates;

public sealed class PromptTemplateProvider
{
    private readonly string _templateRoot;

    public PromptTemplateProvider(string? templateRoot = null)
    {
        _templateRoot = templateRoot ?? Path.Combine(AppContext.BaseDirectory, "Templates");
    }

    public string GetSummarySystem() => ReadOrDefault("summary_system.txt", "You are a meeting summarization assistant. Return ONLY valid JSON.");

    public string GetSummaryUser(string title, string dateIso, string transcript)
    {
        var template = ReadOrDefault("summary_user.txt", "Meeting Title: {{TITLE}}\nDate: {{DATE_ISO}}\n\nTranscript:\n{{TRANSCRIPT}}\n\nReturn JSON only.");
        return template.Replace("{{TITLE}}", title).Replace("{{DATE_ISO}}", dateIso).Replace("{{TRANSCRIPT}}", transcript);
    }

    public string GetRepairSystem() => ReadOrDefault("summary_repair_system.txt", "Your previous output was invalid JSON. Return ONLY valid JSON matching the schema. No commentary.");

    private string ReadOrDefault(string fileName, string fallback)
    {
        var path = Path.Combine(_templateRoot, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : fallback;
    }
}
