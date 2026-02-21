using MeetingScribe.Core.Models;

namespace MeetingScribe.Core.Whisper;

public sealed class WhisperCommandBuilder
{
    public string Build(string executablePath, string wavPath, string outputBasePath, AppSettings settings)
    {
        var args = new List<string>
        {
            $"-m \"{settings.WhisperModelPath}\"",
            $"-f \"{wavPath}\"",
            $"-l {settings.Language}",
            "--output-json",
            $"--output-file \"{outputBasePath}\""
        };

        if (settings.UseVad)
        {
            args.Add("--vad");
            args.Add($"--vad-model \"{settings.VadModelPath}\"");
            args.Add($"--vad-threshold {settings.VadThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            args.Add($"--vad-min-speech-duration-ms {settings.VadMinSpeechMs}");
            args.Add($"--vad-min-silence-duration-ms {settings.VadMinSilenceMs}");
            args.Add($"--vad-max-speech-duration-s {settings.VadMaxSpeechSec}");
            args.Add($"--vad-speech-pad-ms {settings.VadSpeechPadMs}");
            args.Add($"--vad-samples-overlap {settings.VadSamplesOverlap}");
        }

        return $"\"{executablePath}\" {string.Join(' ', args)}";
    }
}
