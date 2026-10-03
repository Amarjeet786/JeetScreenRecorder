using JeetScreenRecorder.Models;

namespace JeetScreenRecorder.Recording;

public interface IRecordingService
{
    RecordingState State { get; }
    TimeSpan Elapsed { get; }
    event EventHandler? StateChanged;
    Task StartAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
}
