using System.Diagnostics;

namespace MeetingScribe.Core.Whisper;

public sealed class WhisperRunner
{
    public async Task<string> RunAsync(string command, string expectedJsonPath, Action<int?> onProgress, CancellationToken ct)
    {
        var (fileName, arguments) = ParseCommand(command);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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

    internal static (string FileName, string Arguments) ParseCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be null or whitespace.", nameof(command));
        }

        var trimmed = command.TrimStart();
        if (trimmed[0] == '"')
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                throw new ArgumentException("Command executable path contains an unterminated quote.", nameof(command));
            }

            var executable = trimmed[1..closingQuote];
            var args = trimmed[(closingQuote + 1)..].TrimStart();
            return (executable, args);
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].TrimStart());
    }
}
