using System.Diagnostics;

namespace MeetingScribe.Core.Whisper;

public sealed class WhisperRunner
{
    public async Task<string> RunAsync(string command, string expectedJsonPath, Action<int?> onProgress, CancellationToken ct)
    {
        var split = command.Split(' ', 2);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = split[0].Trim('"'),
                Arguments = split.Length > 1 ? split[1] : string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var parser = new WhisperProgressParser();
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                onProgress(parser.TryParsePercentage(e.Data));
            }
        };

        process.Start();
        process.BeginOutputReadLine();

        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best effort kill on cancellation
            }
        });

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"whisper-cli failed with code {process.ExitCode}. {err}");
        }

        return await File.ReadAllTextAsync(expectedJsonPath, ct);
    }
}
