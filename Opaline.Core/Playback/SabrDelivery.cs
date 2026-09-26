using System.Net;
using System.Text;

namespace Opaline.Core.Playback;

/// <summary>
/// SABR delivery scaffold (iOS SABRDelivery + LocalMediaServer).
/// Full UMP demux is not yet ported; this hosts a localhost proxy that:
/// 1) Prefer HLS/DASH if the player already exposed them
/// 2) Otherwise reverse-proxies the first progressive URL
/// 3) Records serverAbrStreamingUrl for a future UMP pipeline
/// </summary>
public sealed class SabrDelivery : IDisposable
{
    private HttpListener? _listener;
    private readonly HttpClient _http = new();
    private int _port;
    private string? _upstream;

    public bool IsSupported => true; // localhost proxy path is available
    public bool IsUmpImplemented => false;
    public string? ServerAbrStreamingUrl { get; private set; }
    public string? LocalManifestUrl { get; private set; }

    public async Task<string?> TryCreateLocalManifestAsync(
        string videoId,
        string? serverAbrUrl,
        string? hlsUrl,
        string? progressiveUrl,
        CancellationToken ct = default)
    {
        ServerAbrStreamingUrl = serverAbrUrl;

        // Prefer real HLS (native AdaptiveMediaSource)
        if (!string.IsNullOrEmpty(hlsUrl))
        {
            LocalManifestUrl = hlsUrl;
            return hlsUrl;
        }

        var upstream = progressiveUrl ?? serverAbrUrl;
        if (string.IsNullOrEmpty(upstream))
            return null;

        Stop();
        _upstream = upstream;
        _port = await StartListenerAsync(ct).ConfigureAwait(false);
        LocalManifestUrl = $"http://127.0.0.1:{_port}/media/{Uri.EscapeDataString(videoId)}";
        return LocalManifestUrl;
    }

    private async Task<int> StartListenerAsync(CancellationToken ct)
    {
        // Try a few ports
        Exception? last = null;
        for (var port = 18765; port < 18775; port++)
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Start();
                _listener = l;
                _ = Task.Run(() => AcceptLoop(l, ct), ct);
                return port;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        throw new InvalidOperationException("SABR local proxy: no free port", last);
    }

    private async Task AcceptLoop(HttpListener listener, CancellationToken ct)
    {
        while (listener.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_upstream is null)
                    {
                        ctx.Response.StatusCode = 503;
                        ctx.Response.Close();
                        return;
                    }

                    using var upstreamReq = new HttpRequestMessage(HttpMethod.Get, _upstream);
                    // Forward Range if present (seek)
                    var range = ctx.Request.Headers["Range"];
                    if (!string.IsNullOrEmpty(range))
                        upstreamReq.Headers.TryAddWithoutValidation("Range", range);

                    using var upstreamResp = await _http.SendAsync(
                        upstreamReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    ctx.Response.StatusCode = (int)upstreamResp.StatusCode;
                    if (upstreamResp.Content.Headers.ContentType is not null)
                        ctx.Response.ContentType = upstreamResp.Content.Headers.ContentType.ToString();
                    if (upstreamResp.Content.Headers.ContentLength is long cl)
                        ctx.Response.ContentLength64 = cl;
                    if (upstreamResp.Headers.Contains("Accept-Ranges"))
                        ctx.Response.Headers["Accept-Ranges"] = "bytes";

                    await using var src = await upstreamResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await src.CopyToAsync(ctx.Response.OutputStream, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                }
                catch
                {
                    try { ctx.Response.StatusCode = 502; ctx.Response.Close(); } catch { /* ignore */ }
                }
            }, ct);
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _listener?.Close(); } catch { /* ignore */ }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
