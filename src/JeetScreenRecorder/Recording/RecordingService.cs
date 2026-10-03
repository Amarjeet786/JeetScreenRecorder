using System.Diagnostics;
using JeetScreenRecorder.Models;
using JeetScreenRecorder.Utils;

namespace JeetScreenRecorder.Recording;

/// <summary>
/// Stage 1: state machine + timer only. Capture/encoder/audio pipeline is wired in Stage 2+.
/// </summary>
public sealed class RecordingService : IRecordingService
{
    private readonly Stopwatch _clock = new();
    public RecordingState State { get; private set; } = RecordingState.Idle;
    public TimeSpan Elapsed => _clock.Elapsed;
    public event EventHandler? StateChanged;

    public Task StartAsync()
    {
        if (State != RecordingState.Idle) return Task.CompletedTask;
        _clock.Restart();
        Set(RecordingState.Recording);
        AppLogger.Info("Recording started");
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (State != RecordingState.Recording) return Task.CompletedTask;
        _clock.Stop();
        Set(RecordingState.Paused);
        AppLogger.Info("Recording paused");
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (State != RecordingState.Paused) return Task.CompletedTask;
        _clock.Start();
        Set(RecordingState.Recording);
        AppLogger.Info("Recording resumed");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (State == RecordingState.Idle) return Task.CompletedTask;
        _clock.Stop();
        AppLogger.Info($"Recording stopped after {_clock.Elapsed}");
        Set(RecordingState.Idle);
        return Task.CompletedTask;
    }

    private void Set(RecordingState s)
    {
        State = s;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
