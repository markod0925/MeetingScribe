using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MeetingScribe.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _language = "it";
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language
    {
        get => _language;
        set
        {
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }
}
