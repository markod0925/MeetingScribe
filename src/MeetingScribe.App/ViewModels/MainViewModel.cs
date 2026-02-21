using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using MeetingScribe.App.Commands;
using MeetingScribe.Core.Models;

namespace MeetingScribe.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private AppState _currentState = AppState.Idle;
    private int _progress;
    private readonly StringBuilder _log = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentState => _currentState.ToString();
    public int Progress { get => _progress; private set { _progress = value; OnChanged(); } }
    public string StatusLog => _log.ToString();

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand CancelCommand { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(() => SetState(AppState.Recording));
        StopCommand = new RelayCommand(() => SetState(AppState.Processing));
        CancelCommand = new RelayCommand(() => SetState(AppState.Cancelling));
    }

    private void SetState(AppState state)
    {
        _currentState = state;
        _log.AppendLine($"{DateTime.Now:T} -> {state}");
        if (state == AppState.Recording) Progress = 5;
        if (state == AppState.Processing) Progress = 50;
        if (state == AppState.Done) Progress = 100;
        OnChanged(nameof(CurrentState));
        OnChanged(nameof(StatusLog));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
