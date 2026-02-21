using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using MeetingScribe.App.Commands;
using MeetingScribe.Core.Audio;
using MeetingScribe.Core.Export;
using MeetingScribe.Core.Llm;
using MeetingScribe.Core.Models;
using MeetingScribe.Core.Pipeline;
using MeetingScribe.Core.Settings;
using MeetingScribe.Core.Templates;
using MeetingScribe.Core.Temp;
using MeetingScribe.Core.Transcript;
using MeetingScribe.Core.Whisper;

namespace MeetingScribe.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService = new();
    private readonly TempFileService _tempService = new();
    private readonly AudioCaptureService _audioCapture = new();
    private readonly AudioResampler _resampler = new();
    private readonly PipelineOrchestrator _pipeline;
    private readonly StringBuilder _log = new();

    private AppState _currentState = AppState.Idle;
    private int _progress;
    private string? _runFolder;
    private AppSettings _settings = new();
    private CancellationTokenSource? _processingCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentState => _currentState.ToString();
    public int Progress { get => _progress; private set { _progress = value; OnChanged(); } }
    public string StatusLog => _log.ToString();

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand CancelCommand { get; }

    public MainViewModel()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _pipeline = new PipelineOrchestrator(
            new WhisperCommandBuilder(),
            new WhisperRunner(),
            new WhisperTranscriptParser(),
            new TranscriptMergeEngine(),
            new ChunkingService(),
            new LmStudioClient(http),
            new SummaryMergeService(),
            new MarkdownExporter(),
            new PromptTemplateProvider(Path.Combine(AppContext.BaseDirectory, "Templates")));

        _pipeline.StateChanged += OnPipelineStateChanged;

        StartCommand = new RelayCommand(() => _ = StartAsync(), () => _currentState is AppState.Idle or AppState.Done or AppState.Error);
        StopCommand = new RelayCommand(() => _ = StopAsync(), () => _currentState == AppState.Recording);
        CancelCommand = new RelayCommand(CancelProcessing, () => _currentState is AppState.Processing or AppState.TranscribingMic or AppState.TranscribingLoopback or AppState.Merging or AppState.Summarizing or AppState.Exporting);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _settings = await _settingsService.LoadAsync(AppContext.BaseDirectory, CancellationToken.None);
            _tempService.CleanupOldRuns(_settings.TempRetentionDays);
            Log("Settings loaded and temp cleanup completed.");
        }
        catch (Exception ex)
        {
            Log($"Initialization error: {ex.Message}");
            SetState(AppState.Error);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            _runFolder = _tempService.CreateRunFolder();
            _audioCapture.Start(_runFolder);
            Progress = 5;
            SetState(AppState.Recording);
            Log($"Recording started: {_runFolder}");
        }
        catch (Exception ex)
        {
            Log($"Start failed: {ex.Message}");
            SetState(AppState.Error);
        }

        await Task.CompletedTask;
    }

    private async Task StopAsync()
    {
        if (_runFolder is null)
        {
            Log("No active run folder.");
            return;
        }

        try
        {
            _audioCapture.Stop();
            Log("Recording stopped. Resampling...");

            _resampler.ResampleTo16kMono(Path.Combine(_runFolder, "mic_raw.wav"), Path.Combine(_runFolder, "mic.wav"));
            _resampler.ResampleTo16kMono(Path.Combine(_runFolder, "loopback_raw.wav"), Path.Combine(_runFolder, "loopback.wav"));

            var metadata = _audioCapture.Metadata ?? new RecordingSyncMetadata
            {
                RecordingStartUtc = DateTime.UtcNow,
                MicFirstSampleTicks = 0,
                LoopbackFirstSampleTicks = 0
            };

            _processingCts = new CancellationTokenSource();
            SetState(AppState.Processing);

            var whisperExe = Path.Combine(AppContext.BaseDirectory, _settings.WhisperExecutablePath);
            var exported = await _pipeline.ProcessAsync(_runFolder, whisperExe, _settings, metadata, _processingCts.Token);

            Progress = 100;
            Log($"Export completed: {exported}");
        }
        catch (OperationCanceledException)
        {
            Log("Processing cancelled by user.");
            SetState(AppState.Cancelling);
        }
        catch (Exception ex)
        {
            Log($"Stop/processing failed: {ex.Message}");
            SetState(AppState.Error);
        }
    }

    private void CancelProcessing()
    {
        _processingCts?.Cancel();
        SetState(AppState.Cancelling);
        Log("Cancellation requested.");
    }

    private void OnPipelineStateChanged(AppState state)
    {
        SetState(state);
        Progress = state switch
        {
            AppState.TranscribingMic => 35,
            AppState.TranscribingLoopback => 55,
            AppState.Merging => 70,
            AppState.Summarizing => 85,
            AppState.Exporting => 95,
            AppState.Done => 100,
            _ => Progress
        };
    }

    private void SetState(AppState state)
    {
        _currentState = state;
        OnChanged(nameof(CurrentState));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void Log(string message)
    {
        _log.AppendLine($"{DateTime.Now:T} - {message}");
        OnChanged(nameof(StatusLog));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
