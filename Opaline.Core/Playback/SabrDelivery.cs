namespace Opaline.Core.Playback;

/// <summary>
/// SABR (Server Adaptive BitRate) delivery — iOS implements full SABR via
/// local media server + protobuf UMP segments (SABRDelivery.swift).
/// Windows currently falls back to progressive / HLS / dual-stream.
/// This type is the extension point for a future UMP→MPEG-TS local proxy.
/// </summary>
public sealed class SabrDelivery
{
    public bool IsSupported => false;

    public Task<string?> TryCreateLocalManifestAsync(
        string videoId,
        string? serverAbrUrl,
        CancellationToken ct = default)
    {
        // Full port requires: MediaHeader protobuf, LocalMediaServer HTTP,
        // n-param solve per segment, and AdaptiveMediaSource over localhost.
        return Task.FromResult<string?>(null);
    }
}
