namespace JeetScreenRecorder.Models;

public enum CaptureBackend { DesktopDuplication, Gdigrab }

public sealed record EncoderOptions
{
    public CaptureBackend Backend { get; init; } = CaptureBackend.DesktopDuplication;
    public string EncoderId { get; init; } = "libx264";
    public int Fps { get; init; } = 60;
    public int MonitorIndex { get; init; }
    public int CaptureX { get; init; }          // monitor origin (used by GDI capture)
    public int CaptureY { get; init; }
    public int SourceWidth { get; init; } = 1920;
    public int SourceHeight { get; init; } = 1080;
    public int OutputWidth { get; init; }       // 0 = original
    public int OutputHeight { get; init; }      // 0 = original
    public int BitrateKbps { get; init; } = 12000;
    public bool DrawMouse { get; init; } = true;
    public string? AudioPipePath { get; init; } // null = no audio
    public int AudioSampleRate { get; init; } = 48000;
    public int AudioBitrateKbps { get; init; } = 192;
}
