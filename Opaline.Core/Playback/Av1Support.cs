namespace Opaline.Core.Playback;

/// <summary>
/// AV1 decode preference (iOS AV1Support via VTIsHardwareDecodeSupported).
/// Windows Media Foundation / MediaPlayer decode AV1 on Win10 20H2+ with
/// hardware or software decoder; we gate selection via preference + heuristic.
/// </summary>
public static class Av1Support
{
    /// <summary>User preference: allow selecting av01 formats (default true on x64).</summary>
    public static bool IsPreferred { get; set; } = true;

    /// <summary>
    /// Whether AV1 formats may enter the quality ladder.
    /// Conservative: prefer when user enabled; Windows generally has AV1 decode.
    /// </summary>
    public static bool IsSupported => IsPreferred;

    public static bool AllowsMime(string? mimeOrCodecs)
    {
        if (string.IsNullOrEmpty(mimeOrCodecs)) return true;
        var m = mimeOrCodecs.ToLowerInvariant();
        if (m.Contains("av01") || m.Contains("av1"))
            return IsSupported;
        return true;
    }
}
