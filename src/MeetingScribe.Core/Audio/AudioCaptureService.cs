using MeetingScribe.Core.Models;
using NAudio.Wave;

namespace MeetingScribe.Core.Audio;

public sealed class AudioCaptureService : IDisposable
{
    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loop;
    private WaveFileWriter? _micWriter;
    private WaveFileWriter? _loopWriter;
    private long? _micFirst;
    private long? _loopFirst;

    public RecordingSyncMetadata? Metadata { get; private set; }

    public void Start(string runFolder)
    {
        _mic = new WasapiCapture();
        _loop = new WasapiLoopbackCapture();
        _micWriter = new WaveFileWriter(Path.Combine(runFolder, "mic_raw.wav"), _mic.WaveFormat);
        _loopWriter = new WaveFileWriter(Path.Combine(runFolder, "loopback_raw.wav"), _loop.WaveFormat);

        var startUtc = DateTime.UtcNow;

        _mic.DataAvailable += (_, a) =>
        {
            _micFirst ??= System.Diagnostics.Stopwatch.GetTimestamp();
            _micWriter?.Write(a.Buffer, 0, a.BytesRecorded);
            TrySetMetadata(startUtc);
        };

        _loop.DataAvailable += (_, a) =>
        {
            _loopFirst ??= System.Diagnostics.Stopwatch.GetTimestamp();
            _loopWriter?.Write(a.Buffer, 0, a.BytesRecorded);
            TrySetMetadata(startUtc);
        };

        _mic.StartRecording();
        _loop.StartRecording();
    }

    public void Stop()
    {
        _mic?.StopRecording();
        _loop?.StopRecording();
        _micWriter?.Dispose();
        _loopWriter?.Dispose();
    }

    private void TrySetMetadata(DateTime startUtc)
    {
        if (_micFirst is null || _loopFirst is null || Metadata is not null) return;
        Metadata = new RecordingSyncMetadata
        {
            RecordingStartUtc = startUtc,
            MicFirstSampleTicks = _micFirst.Value,
            LoopbackFirstSampleTicks = _loopFirst.Value
        };
    }

    public void Dispose() => Stop();
}
