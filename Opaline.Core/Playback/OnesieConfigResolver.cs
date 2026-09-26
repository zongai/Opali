using Opaline.Core.Models;

namespace Opaline.Core.Playback;

/// <summary>
/// Resolves the ustreamer config blob used for SABR / googlevideo ABR.
/// iOS extracts both <c>videoPlaybackUstreamerConfig</c> and
/// <c>onesieUstreamerConfig</c> from playerConfig; only the former is required
/// by SABRDelivery.canServe. When playback config is missing, Onesie is the
/// documented alternate field in mediaUstreamerRequestConfig (same base64 shape).
/// There is no separate Onesie HTTP delivery in the iOS tree — only this config.
/// </summary>
public static class OnesieConfigResolver
{
    public enum ConfigSource
    {
        None,
        VideoPlaybackUstreamer,
        OnesieUstreamer,
    }

    public readonly record struct Resolved(
        byte[] Bytes,
        ConfigSource Source,
        string? RawBase64);

    /// <summary>
    /// Prefer videoPlaybackUstreamerConfig; fall back to onesieUstreamerConfig.
    /// Both are web-safe base64 (possibly unpadded).
    /// </summary>
    public static Resolved? TryResolve(WatchPage page)
    {
        if (TryDecode(page.VideoPlaybackUstreamerConfig, out var playback))
            return new Resolved(playback, ConfigSource.VideoPlaybackUstreamer, page.VideoPlaybackUstreamerConfig);

        if (TryDecode(page.OnesieUstreamerConfig, out var onesie))
            return new Resolved(onesie, ConfigSource.OnesieUstreamer, page.OnesieUstreamerConfig);

        return null;
    }

    public static bool TryDecode(string? b64, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(b64))
            return false;
        try
        {
            var s = b64.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            bytes = Convert.FromBase64String(s);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
