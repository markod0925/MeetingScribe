using NAudio.Wave;

namespace MeetingScribe.Core.Audio;

public sealed class AudioResampler
{
    public void ResampleTo16kMono(string inputPath, string outputPath)
    {
        using var reader = new AudioFileReader(inputPath);
        var outFormat = new WaveFormat(16000, 16, 1);
        using var resampler = new MediaFoundationResampler(reader, outFormat) { ResamplerQuality = 60 };
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
    }
}
