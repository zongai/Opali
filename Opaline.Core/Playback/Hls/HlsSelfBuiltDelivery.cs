using System.Net;
using System.Text;
using Opaline.Core.Models;

namespace Opaline.Core.Playback.Hls;

/// <summary>
/// Builds local HLS from adaptive format index (SIDX) + byte-range media playlists
/// (iOS HLSPlaybackBuilder / HLSGenerator).
/// </summary>
public sealed class HlsSelfBuiltDelivery : IDisposable
{
    private readonly HttpClient _http;
    private HttpListener? _listener;
    private int _port;
    private string? _master;
    private string? _videoPl;
    private string? _audioPl;

    public HlsSelfBuiltDelivery(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string? LocalMasterUrl { get; private set; }
    public string? LastError { get; private set; }

    public async Task<string?> TryBuildAsync(
        StreamInfo video,
        StreamInfo audio,
        string? videoUrl = null,
        string? audioUrl = null,
        CancellationToken ct = default)
    {
        videoUrl ??= video.Url;
        audioUrl ??= audio.Url;
        if (string.IsNullOrEmpty(videoUrl) || string.IsNullOrEmpty(audioUrl))
        {
            LastError = "missing adaptive URLs";
            return null;
        }
        if (video.IndexRangeEnd <= 0 || audio.IndexRangeEnd <= 0)
        {
            LastError = "missing indexRange on formats";
            return null;
        }

        try
        {
            var vIdx = await FetchRangeAsync(videoUrl, video.IndexRangeStart, video.IndexRangeEnd, ct)
                .ConfigureAwait(false);
            var aIdx = await FetchRangeAsync(audioUrl, audio.IndexRangeStart, audio.IndexRangeEnd, ct)
                .ConfigureAwait(false);
            if (vIdx is null || aIdx is null)
            {
                LastError = "SIDX range fetch failed";
                return null;
            }

            var vSeg = SidxParser.Parse(vIdx);
            var aSeg = SidxParser.Parse(aIdx);
            if (vSeg is null || aSeg is null || vSeg.Count == 0 || aSeg.Count == 0)
            {
                LastError = "SIDX parse failed";
                return null;
            }

            var vInit = video.InitRangeEnd > 0 ? video.InitRangeEnd + 1 : video.IndexRangeStart;
            var aInit = audio.InitRangeEnd > 0 ? audio.InitRangeEnd + 1 : audio.IndexRangeStart;
            var vDataStart = (long)video.IndexRangeEnd + 1;
            var aDataStart = (long)audio.IndexRangeEnd + 1;

            // Absolute media URLs — AdaptiveMediaSource fetches byte ranges directly
            _videoPl = HlsPlaylistGenerator.MediaPlaylist(videoUrl, vInit, vDataStart, vSeg);
            _audioPl = HlsPlaylistGenerator.MediaPlaylist(audioUrl, aInit, aDataStart, aSeg);

            var bw = HlsPlaylistGenerator.PeakBitrate(vSeg, (int)(video.Bitrate ?? 1_000_000));
            var codecs = $"{ExtractCodec(video.MimeType)},{ExtractCodec(audio.MimeType)}".Trim(',');
            var res = $"{video.Width ?? 1280}x{video.Height ?? 720}";

            Stop();
            _port = await StartListenerAsync(ct).ConfigureAwait(false);
            var baseUri = $"http://127.0.0.1:{_port}/hls";
            _master = HlsPlaylistGenerator.MasterPlaylist(
                bw, codecs, res, $"{baseUri}/video.m3u8", $"{baseUri}/audio.m3u8");
            LocalMasterUrl = $"{baseUri}/master.m3u8";
            return LocalMasterUrl;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    private async Task<byte[]?> FetchRangeAsync(string url, int start, int end, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Range", $"bytes={start}-{end}");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private static string ExtractCodec(string? mime)
    {
        if (string.IsNullOrEmpty(mime)) return "";
        var i = mime.IndexOf("codecs=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return mime.Contains("mp4a", StringComparison.OrdinalIgnoreCase) ? "mp4a.40.2" : "avc1.64001f";
        var s = mime[(i + 7)..].Trim().Trim('"');
        var end = s.IndexOfAny([' ', ';', ',']);
        return end > 0 ? s[..end].Trim('"') : s.Trim('"');
    }

    private async Task<int> StartListenerAsync(CancellationToken ct)
    {
        Exception? last = null;
        for (var port = 18865; port < 18885; port++)
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
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("HLS local server: no free port", last);
    }

    private async Task AcceptLoop(HttpListener listener, CancellationToken ct)
    {
        while (listener.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => Serve(ctx), ct);
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            string? body = path switch
            {
                var p when p.EndsWith("master.m3u8", StringComparison.OrdinalIgnoreCase) => _master,
                var p when p.EndsWith("video.m3u8", StringComparison.OrdinalIgnoreCase) => _videoPl,
                var p when p.EndsWith("audio.m3u8", StringComparison.OrdinalIgnoreCase) => _audioPl,
                _ => null
            };
            if (body is null)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/vnd.apple.mpegurl";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* */ }
        }
    }

    public void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { /* */ }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
