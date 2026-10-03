using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using JeetScreenRecorder.Models;
using JeetScreenRecorder.Recording;
using JeetScreenRecorder.Settings;
using JeetScreenRecorder.Utils;

namespace JeetScreenRecorder.UI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IRecordingService _rec;
    private readonly ISettingsService _settings;
    private readonly RecordingSettings _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _message = "";

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public int[] FpsOptions { get; } = { 24, 30, 48, 50, 60 };
    public string[] ResolutionOptions { get; } =
        { "Original", "3840×2160", "2560×1440", "1920×1080", "1600×900", "1280×720", "854×480" };

    public MainViewModel(IRecordingService rec, ISettingsService settings)
    {
        _rec = rec;
        _settings = settings;
        _s = settings.Current;

        StartCommand = new RelayCommand(() => Run(_rec.StartAsync), () => _rec.State == RecordingState.Idle);
        PauseResumeCommand = new RelayCommand(
            () => Run(() => _rec.State == RecordingState.Paused ? _rec.ResumeAsync() : _rec.PauseAsync()),
            () => _rec.State is RecordingState.Recording or RecordingState.Paused);
        StopCommand = new RelayCommand(() => Run(_rec.StopAsync),
            () => _rec.State is RecordingState.Recording or RecordingState.Paused);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        _rec.StateChanged += (_, _) => Refresh();
        _rec.Notice += (_, msg) => Application.Current.Dispatcher.Invoke(() => Message = msg);
        _timer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(TimerText));
            OnPropertyChanged(nameof(StatsText));
            OnPropertyChanged(nameof(EncoderText));
        };
        _timer.Start();

        Run(_rec.InitializeAsync);
    }

    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            AppLogger.Error("Action failed", ex);
            Message = ex.Message;
            Refresh();
        }
    }

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public int SelectedFps
    {
        get => _s.Fps;
        set { if (_s.Fps == value) return; _s.Fps = value; Save(); OnPropertyChanged(); }
    }

    public string SelectedResolution
    {
        get => _s.Width == 0 ? "Original" : $"{_s.Width}×{_s.Height}";
        set
        {
            if (value == null || value == SelectedResolution) return;
            if (value == "Original") { _s.Width = 0; _s.Height = 0; Message = ""; }
            else
            {
                var p = value.Split('×');
                _s.Width = int.Parse(p[0]);
                _s.Height = int.Parse(p[1]);
                var (sw, sh) = ScreenInfo.PrimarySize();
                Message = _s.Width > sw || _s.Height > sh
                    ? $"Your screen is {sw}×{sh}. The video will not be upscaled and will be recorded at the original size."
                    : "";
            }
            Save();
            OnPropertyChanged();
        }
    }

    public bool CanEditSettings => _rec.State == RecordingState.Idle;

    public string TimerText => _rec.Elapsed.ToString(@"hh\:mm\:ss");

    public string StatusText => _rec.State switch
    {
        RecordingState.Recording => "● Recording",
        RecordingState.Paused => "⏸ Paused",
        RecordingState.Finalizing => "Saving video…",
        _ => "Ready"
    };

    public string PauseLabel => _rec.State == RecordingState.Paused ? "▶ Resume" : "⏸ Pause";

    public string EncoderText => $"Encoder: {_rec.EncoderName}";
    public string OutputText => $"Saving to: {_s.OutputFolder}";

    public string StatsText
    {
        get
        {
            if (_rec.State == RecordingState.Idle || _rec.State == RecordingState.Finalizing)
                return "Choose FPS and resolution, then press Start Recording.";
            var st = _rec.Stats;
            var text = $"{st.Resolution}  •  FPS: {st.Fps:0.0}  •  Size: {FormatSize(st.SizeBytes)}  •  Dropped: {st.Dropped}";
            if (_rec.State == RecordingState.Recording && _rec.Elapsed.TotalSeconds > 5 && st.Fps > 0 && st.Fps < _s.Fps * 0.9)
                text += "\n⚠ Performance issue detected. Try reducing resolution or encoder quality.";
            return text;
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.00} GB" : $"{bytes / 1048576.0:0.0} MB";

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_s.OutputFolder);
            var last = _rec.LastOutputPath;
            var args = last != null && File.Exists(last) ? $"/select,\"{last}\"" : $"\"{_s.OutputFolder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { Message = ex.Message; }
    }

    private void Save()
    {
        try { _settings.Save(); }
        catch (Exception ex) { AppLogger.Error("Saving settings failed", ex); }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TimerText));
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(EncoderText));
        StartCommand.Raise();
        PauseResumeCommand.Raise();
        StopCommand.Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
