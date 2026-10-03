using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using JeetScreenRecorder.Audio;
using JeetScreenRecorder.Capture;
using JeetScreenRecorder.Hotkeys;
using JeetScreenRecorder.Models;
using JeetScreenRecorder.Recording;
using JeetScreenRecorder.Settings;
using JeetScreenRecorder.Utils;

namespace JeetScreenRecorder.UI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IRecordingService _rec;
    private readonly ISettingsService _settings;
    private readonly IAudioCaptureService _audio;
    private readonly IScreenshotService _shot;
    private readonly RecordingSettings _s;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _message = "";
    private int _countdown;
    private double _micLevel, _sysLevel;

    public RelayCommand StartCommand { get; }
    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ScreenshotCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand TestAudioCommand { get; }

    public int[] FpsOptions { get; } = { 24, 30, 48, 50, 60 };
    public string[] ResolutionOptions { get; } =
        { "Original", "3840×2160", "2560×1440", "1920×1080", "1600×900", "1280×720", "854×480" };
    public string[] QualityOptions { get; } = { "Low", "Medium", "High", "Very High", "Lossless" };
    public string[] CaptureMethodOptions { get; } = { "Auto (GPU – fastest)", "Compatible (any screen)" };
    public string[] CountdownOptions { get; } = { "Off", "3 seconds", "5 seconds", "10 seconds" };
    public IReadOnlyList<MonitorInfo> MonitorOptions { get; }
    public IReadOnlyList<AudioDeviceInfo> MicOptions { get; }

    public MainViewModel(IRecordingService rec, ISettingsService settings, IAudioCaptureService audio,
        IScreenshotService shot, IMonitorService monitors)
    {
        _rec = rec;
        _settings = settings;
        _audio = audio;
        _shot = shot;
        _s = settings.Current;
        MonitorOptions = monitors.GetMonitors();
        MicOptions = audio.GetMicrophones();
        _audio.SetGains(_s.MicVolume, _s.SystemVolume);

        StartCommand = new RelayCommand(() => Run(StartWithCountdownAsync),
            () => _rec.State == RecordingState.Idle && _countdown == 0);
        PauseResumeCommand = new RelayCommand(
            () => Run(() => _rec.State == RecordingState.Paused ? _rec.ResumeAsync() : _rec.PauseAsync()),
            () => _rec.State is RecordingState.Recording or RecordingState.Paused);
        StopCommand = new RelayCommand(() => Run(_rec.StopAsync),
            () => _rec.State is RecordingState.Recording or RecordingState.Paused);
        ScreenshotCommand = new RelayCommand(() => Run(TakeScreenshotAsync));
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ChooseFolderCommand = new RelayCommand(ChooseFolder, () => _rec.State == RecordingState.Idle);
        TestAudioCommand = new RelayCommand(ToggleTestAudio, () => _rec.State == RecordingState.Idle);

        _rec.StateChanged += (_, _) => Refresh();
        _rec.Notice += (_, msg) => Application.Current.Dispatcher.Invoke(() => Message = msg);
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        Run(_rec.InitializeAsync);
    }

    // ---------- helpers ----------
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

    private async Task StartWithCountdownAsync()
    {
        PersistSettings();
        int n = _s.CountdownSeconds;
        if (n > 0)
        {
            try
            {
                for (int i = n; i > 0; i--)
                {
                    Countdown = i;
                    await Task.Delay(1000);
                }
            }
            finally { Countdown = 0; }
        }
        await _rec.StartAsync();
    }

    private async Task TakeScreenshotAsync()
    {
        var path = await _shot.CaptureAsync();
        Message = $"Screenshot saved: {path}";
    }

    private void OnTick()
    {
        var (m, s) = _audio.ReadPeaks();
        _micLevel = Math.Max(Math.Sqrt(m) * 100, _micLevel * 0.85);
        _sysLevel = Math.Max(Math.Sqrt(s) * 100, _sysLevel * 0.85);
        OnPropertyChanged(nameof(MicLevel));
        OnPropertyChanged(nameof(SystemLevel));
        OnPropertyChanged(nameof(TimerText));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(EncoderText));
    }

    private void ToggleTestAudio()
    {
        if (_rec.State != RecordingState.Idle) return;
        if (_audio.IsRunning) _audio.Stop();
        else
        {
            _audio.SetGains(_s.MicVolume, _s.SystemVolume);
            _audio.Start(_s.MicDeviceId, _s.MicEnabled, _s.SystemAudioEnabled, _s.AudioSampleRate);
        }
        OnPropertyChanged(nameof(TestLabel));
    }

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

    private void ChooseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where to save recordings",
            InitialDirectory = _s.OutputFolder
        };
        if (dlg.ShowDialog() == true)
        {
            _s.OutputFolder = dlg.FolderName;
            PersistSettings();
            OnPropertyChanged(nameof(OutputText));
        }
    }

    public void RegisterHotkeys(IHotkeyService hk)
    {
        var map = new (string Name, Action Action)[]
        {
            ("StartStop", ToggleRecording),
            ("PauseResume", () => TryExecute(PauseResumeCommand)),
            ("Screenshot", () => TryExecute(ScreenshotCommand))
        };
        foreach (var (name, action) in map)
        {
            if (!_s.Hotkeys.TryGetValue(name, out var gesture)) continue;
            if (!hk.Register(name, gesture, action))
                Message = $"Hotkey {gesture} is already used by another application.";
        }
    }

    private void ToggleRecording()
    {
        if (_rec.State == RecordingState.Idle) TryExecute(StartCommand);
        else TryExecute(StopCommand);
    }

    private static void TryExecute(RelayCommand c)
    {
        if (c.CanExecute(null)) c.Execute(null);
    }

    public bool IsBusy => _rec.State != RecordingState.Idle;

    public void PersistSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { AppLogger.Error("Saving settings failed", ex); }
    }

    public void Shutdown()
    {
        PersistSettings();
        _audio.Stop();
    }

    // ---------- bindable state ----------
    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public int Countdown
    {
        get => _countdown;
        private set
        {
            _countdown = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            StartCommand.Raise();
        }
    }

    public bool CanEditSettings => _rec.State == RecordingState.Idle && _countdown == 0;
    public string TimerText => _rec.Elapsed.ToString(@"hh\:mm\:ss");

    public string StatusText => _countdown > 0
        ? $"Recording starts in {_countdown}…"
        : _rec.State switch
        {
            RecordingState.Recording => "● Recording",
            RecordingState.Paused => "⏸ Paused",
            RecordingState.Finalizing => "Saving video…",
            _ => "Ready"
        };

    public string PauseLabel => _rec.State == RecordingState.Paused ? "▶ Resume" : "⏸ Pause";
    public string TestLabel => _audio.IsRunning && _rec.State == RecordingState.Idle ? "■ Stop audio test" : "🎧 Test audio levels";
    public string EncoderText => $"Encoder: {_rec.EncoderName}  •  Capture: {_rec.CaptureName}";
    public string OutputText => $"Saving to: {_s.OutputFolder}";

    public string StatsText
    {
        get
        {
            if (_rec.State == RecordingState.Idle || _rec.State == RecordingState.Finalizing)
                return "Choose your screen and settings, then press Start Recording (hotkey: F9).";
            var st = _rec.Stats;
            var text = $"{st.Resolution}  •  FPS: {st.Fps:0.0}  •  Size: {FormatSize(st.SizeBytes)}  •  Dropped: {st.Dropped}";
            if (_rec.State == RecordingState.Recording && _rec.Elapsed.TotalSeconds > 5 && st.Fps > 0 && st.Fps < _s.Fps * 0.9)
                text += "\n⚠ Performance issue detected. Try reducing resolution or encoder quality.";
            return text;
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1073741824.0:0.00} GB" : $"{bytes / 1048576.0:0.0} MB";

    public double MicLevel => _micLevel;
    public double SystemLevel => _sysLevel;

    // ---------- settings ----------
    public int SelectedFps
    {
        get => _s.Fps;
        set { if (_s.Fps == value) return; _s.Fps = value; PersistSettings(); OnPropertyChanged(); }
    }

    public string SelectedResolution
    {
        get => _s.Width == 0 ? "Original" : $"{_s.Width}×{_s.Height}";
        set
        {
            if (value == null || value == SelectedResolution) return;
            if (value == "Original") { _s.Width = 0; _s.Height = 0; }
            else
            {
                var p = value.Split('×');
                _s.Width = int.Parse(p[0]);
                _s.Height = int.Parse(p[1]);
            }
            PersistSettings();
            OnPropertyChanged();
            UpdateResolutionWarning();
        }
    }

    public string SelectedQuality
    {
        get => QualityOptions[Math.Clamp((int)_s.Quality, 0, QualityOptions.Length - 1)];
        set
        {
            int i = Array.IndexOf(QualityOptions, value);
            if (i < 0) return;
            _s.Quality = (QualityPreset)i;
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public string SelectedCaptureMethod
    {
        get => _s.CompatibleCapture ? CaptureMethodOptions[1] : CaptureMethodOptions[0];
        set
        {
            if (value == null) return;
            _s.CompatibleCapture = value == CaptureMethodOptions[1];
            PersistSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EncoderText));
        }
    }

    public string SelectedCountdown
    {
        get => _s.CountdownSeconds switch { 3 => "3 seconds", 5 => "5 seconds", 10 => "10 seconds", _ => "Off" };
        set
        {
            _s.CountdownSeconds = value switch { "3 seconds" => 3, "5 seconds" => 5, "10 seconds" => 10, _ => 0 };
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public MonitorInfo? SelectedMonitor
    {
        get => MonitorOptions.FirstOrDefault(m => m.Index == _s.MonitorIndex) ?? MonitorOptions.FirstOrDefault();
        set
        {
            if (value == null) return;
            _s.MonitorIndex = value.Index;
            PersistSettings();
            OnPropertyChanged();
            UpdateResolutionWarning();
        }
    }

    private void UpdateResolutionWarning()
    {
        var mon = SelectedMonitor;
        if (mon != null && _s.Width > 0 && (_s.Width > mon.Width || _s.Height > mon.Height))
            Message = $"This screen is {mon.Width}×{mon.Height}. The video will not be upscaled and will be recorded at the original size.";
        else if (Message.StartsWith("This screen is")) Message = "";
    }

    public bool HideFromCapture
    {
        get => _s.HideFromCapture;
        set { _s.HideFromCapture = value; PersistSettings(); OnPropertyChanged(); }
    }

    // ---------- audio ----------
    public string SelectedMicId
    {
        get => _s.MicDeviceId ?? "";
        set
        {
            _s.MicDeviceId = string.IsNullOrEmpty(value) ? null : value;
            _audio.SetMicDevice(_s.MicDeviceId);
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public bool MicEnabled
    {
        get => _s.MicEnabled;
        set
        {
            _s.MicEnabled = value;
            _audio.SetEnabled(_s.MicEnabled, _s.SystemAudioEnabled);
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public bool SystemEnabled
    {
        get => _s.SystemAudioEnabled;
        set
        {
            _s.SystemAudioEnabled = value;
            _audio.SetEnabled(_s.MicEnabled, _s.SystemAudioEnabled);
            PersistSettings();
            OnPropertyChanged();
        }
    }

    public double MicVolume
    {
        get => _s.MicVolume * 100;
        set
        {
            _s.MicVolume = Math.Clamp(value / 100, 0, 1);
            _audio.SetGains(_s.MicVolume, _s.SystemVolume);
            OnPropertyChanged();
        }
    }

    public double SystemVolume
    {
        get => _s.SystemVolume * 100;
        set
        {
            _s.SystemVolume = Math.Clamp(value / 100, 0, 1);
            _audio.SetGains(_s.MicVolume, _s.SystemVolume);
            OnPropertyChanged();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TimerText));
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(TestLabel));
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(EncoderText));
        StartCommand.Raise();
        PauseResumeCommand.Raise();
        StopCommand.Raise();
        ChooseFolderCommand.Raise();
        TestAudioCommand.Raise();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
