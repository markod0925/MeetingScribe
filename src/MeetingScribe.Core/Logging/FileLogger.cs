namespace MeetingScribe.Core.Logging;

public sealed class FileLogger
{
    private readonly string _logPath;

    public FileLogger(string preferredRoot)
    {
        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingScribe", "logs");
        var targetRoot = CanWrite(preferredRoot) ? preferredRoot : appDataRoot;
        Directory.CreateDirectory(targetRoot);
        _logPath = Path.Combine(targetRoot, "run.log");
    }

    public Task LogAsync(string message, CancellationToken ct = default) =>
        File.AppendAllTextAsync(_logPath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}", ct);

    private static bool CanWrite(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testPath = Path.Combine(path, ".write-test");
            File.WriteAllText(testPath, "ok");
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
