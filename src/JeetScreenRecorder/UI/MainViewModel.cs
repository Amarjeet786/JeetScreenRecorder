using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using JeetScreenRecorder.Models;
using JeetScreenRecorder.Recording;
using JeetScreenRecorder.Settings;
using JeetScreenRecorder.Utils;

namespace JeetScreenRecorder.UI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IRecordingService _rec;
    private readonly RecordingSettings _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCommand { get; }

    public MainViewModel(IRecordingService rec, ISettingsService settings)
    {
        _rec = rec;
        _s = settings.Current;

        StartCommand = new RelayCommand(() => _ = _rec.StartAsync(), () => _rec.State == RecordingState.Idle);
        PauseResumeCommand = new RelayCommand(
            () => _ = _rec.State == RecordingState.Paused ? _rec.ResumeAsync() : _rec.PauseAsync(),
            () => _rec.State != RecordingState.Idle);
        StopCommand = new RelayCommand(() => _ = _rec.StopAsync(), () => _rec.State != RecordingState.Idle);

        _rec.StateChanged += (_, _) => Refresh();
        _timer.Tick += (_, _) => OnPropertyChanged(nameof(TimerText));
        _timer.Start();
    }

    public string TimerText => _rec.Elapsed.ToString(@"hh\:mm\:ss");

    public string StatusText => _rec.State switch
    {
        RecordingState.Recording => "● Recording",
        RecordingState.Paused => "⏸ Paused",
        _ => "Ready"
    };

    public string PauseLabel => _rec.State == RecordingState.Paused ? "▶ Resume" : "⏸ Pause";

    public string InfoText =>
        $"Target: {(_s.Width == 0 ? "Original" : $"{_s.Width}×{_s.Height}")}  •  {_s.Fps} FPS  •  " +
        $"Audio: {(_s.MicEnabled || _s.SystemAudioEnabled ? "ON" : "OFF")}  •  " +
        $"~{SizeEstimator.GbPerHour(SizeEstimator.RecommendedBitrateKbps(1920, 1080, _s.Fps), _s.AudioBitrateKbps):0.0} GB/hour (1080p est.)";

    private void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TimerText));
        OnPropertyChanged(nameof(PauseLabel));
        StartCommand.Raise();
        PauseResumeCommand.Raise();
        StopCommand.Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
