namespace JeetScreenRecorder.Audio;

public enum AudioDeviceKind { Microphone, SystemOutput }
public sealed record AudioDeviceInfo(string Id, string Name, AudioDeviceKind Kind);

public sealed class AudioLevelEventArgs(double micPeak, double systemPeak) : EventArgs
{
    public double MicPeak { get; } = micPeak;
    public double SystemPeak { get; } = systemPeak;
}

public interface IAudioCaptureService : IAsyncDisposable
{
    IReadOnlyList<AudioDeviceInfo> GetDevices();
    event EventHandler<AudioLevelEventArgs>? LevelsChanged;
    Task StartAsync(string? micDeviceId, bool captureSystem, int sampleRate, int channels);
    Task StopAsync();
}
