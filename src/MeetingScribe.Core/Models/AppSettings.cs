namespace MeetingScribe.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "it";
    public string WhisperModelPath { get; set; } = "models/ggml-base.bin";
    public bool UseVad { get; set; } = true;
    public string VadModelPath { get; set; } = "models/vad/ggml-silero-v5.1.2.bin";
    public float VadThreshold { get; set; } = 0.5f;
    public int VadMinSpeechMs { get; set; } = 200;
    public int VadMinSilenceMs { get; set; } = 350;
    public int VadMaxSpeechSec { get; set; } = 15;
    public int VadSpeechPadMs { get; set; } = 150;
    public int VadSamplesOverlap { get; set; } = 1024;
    public string LmStudioBaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
    public string LmModel { get; set; } = "local-model";
    public int StartupRetryCount { get; set; } = 5;
    public int StartupRetryDelaySec { get; set; } = 2;
    public int TempRetentionDays { get; set; } = 2;
    public int MaxCharsPerChunk { get; set; } = 12000;
    public int ChunkOverlapChars { get; set; } = 1200;
}
