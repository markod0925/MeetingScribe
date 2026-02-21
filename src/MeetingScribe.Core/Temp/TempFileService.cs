namespace MeetingScribe.Core.Temp;

public sealed class TempFileService
{
    public string CreateRunFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "MeetingScribe", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void CleanupOldRuns(int retentionDays)
    {
        var root = Path.Combine(Path.GetTempPath(), "MeetingScribe");
        if (!Directory.Exists(root)) return;
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var dir in Directory.GetDirectories(root))
        {
            var info = new DirectoryInfo(dir);
            if (info.CreationTimeUtc < threshold)
            {
                info.Delete(true);
            }
        }
    }
}
