using JeetScreenRecorder.Models;

namespace JeetScreenRecorder.VideoEncoding;

public sealed record EncoderInfo(string Id, string DisplayName, VideoCodec Codec, bool IsHardware);

public interface IVideoEncoder : IAsyncDisposable
{
    Task<IReadOnlyList<EncoderInfo>> DetectEncodersAsync();
    Task StartAsync(RecordingSettings settings, string outputPath);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
}
