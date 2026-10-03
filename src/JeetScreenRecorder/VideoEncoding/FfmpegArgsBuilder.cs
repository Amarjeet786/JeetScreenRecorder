using System.Text;
using JeetScreenRecorder.Models;

namespace JeetScreenRecorder.VideoEncoding;

public static class FfmpegArgsBuilder
{
    public static bool IsNvenc(string id) => id.EndsWith("_nvenc", StringComparison.Ordinal);
    private static bool IsQsv(string id) => id.EndsWith("_qsv", StringComparison.Ordinal);
    private static bool IsAmf(string id) => id.EndsWith("_amf", StringComparison.Ordinal);
    private static bool IsHardware(string id) => IsNvenc(id) || IsQsv(id) || IsAmf(id);

    public static double QualityMultiplier(QualityPreset q) => q switch
    {
        QualityPreset.Low => 0.4,
        QualityPreset.Medium => 0.7,
        QualityPreset.High => 1.0,
        QualityPreset.VeryHigh => 1.6,
        QualityPreset.Lossless => 3.0, // near-lossless (very high bitrate)
        _ => 1.0
    };

    /// <summary>Scale only when the target is smaller than the source (never upscale).</summary>
    public static bool NeedsScale(EncoderOptions o) =>
        o.OutputWidth > 0 && o.OutputHeight > 0 &&
        o.OutputWidth <= o.SourceWidth && o.OutputHeight <= o.SourceHeight &&
        (o.OutputWidth < o.SourceWidth || o.OutputHeight < o.SourceHeight);

    public static (int Width, int Height) OutputSize(EncoderOptions o) =>
        NeedsScale(o) ? (o.OutputWidth, o.OutputHeight) : (o.SourceWidth, o.SourceHeight);

    public static string Build(EncoderOptions o, string? outputPath, bool test = false)
    {
        var sb = new StringBuilder("-hide_banner -y -loglevel error ");
        if (!test) sb.Append("-progress pipe:1 -nostats ");
        int mouse = o.DrawMouse ? 1 : 0;

        if (o.Backend == CaptureBackend.DesktopDuplication)
        {
            sb.Append($"-f lavfi -i \"ddagrab=output_idx={o.MonitorIndex}:framerate={o.Fps}:draw_mouse={mouse}\" ");
            // NVENC can take GPU frames directly (zero-copy) when no scaling is needed.
            bool cpuPath = !IsNvenc(o.EncoderId) || NeedsScale(o);
            if (cpuPath) sb.Append($"-vf \"{CpuFilter(o, fromGpuFrames: true)}\" ");
        }
        else
        {
            sb.Append($"-f gdigrab -framerate {o.Fps} -draw_mouse {mouse} -i desktop ");
            sb.Append($"-vf \"{CpuFilter(o, fromGpuFrames: false)}\" ");
        }

        AppendEncoder(sb, o);
        sb.Append($"-g {o.Fps * 2} ");

        if (test) sb.Append("-frames:v 5 -f null -");
        else sb.Append($"\"{outputPath}\"");
        return sb.ToString();
    }

    private static string CpuFilter(EncoderOptions o, bool fromGpuFrames)
    {
        var f = new List<string>();
        if (fromGpuFrames) { f.Add("hwdownload"); f.Add("format=bgra"); }
        f.Add(NeedsScale(o)
            ? $"scale={o.OutputWidth}:{o.OutputHeight}:flags=bicubic"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2");
        f.Add(IsHardware(o.EncoderId) ? "format=nv12" : "format=yuv420p");
        return string.Join(",", f);
    }

    private static void AppendEncoder(StringBuilder sb, EncoderOptions o)
    {
        int k = o.BitrateKbps, max = k * 3 / 2, buf = k * 2;
        var id = o.EncoderId;
        if (IsNvenc(id))
            sb.Append($"-c:v {id} -preset p4 -rc vbr -b:v {k}k -maxrate {max}k -bufsize {buf}k ");
        else if (IsQsv(id))
            sb.Append($"-c:v {id} -preset medium -b:v {k}k -maxrate {max}k -bufsize {buf}k ");
        else if (IsAmf(id))
            sb.Append($"-c:v {id} -quality balanced -rc vbr_peak -b:v {k}k -maxrate {max}k ");
        else
            sb.Append($"-c:v libx264 -preset veryfast -b:v {k}k -maxrate {max}k -bufsize {buf}k ");
    }
}
