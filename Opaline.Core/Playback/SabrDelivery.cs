using Opaline.Core.Models;
using Opaline.Core.Playback.Sabr;
using System.Net;
using System.Text;

namespace Opaline.Core.Playback;

/// <summary>
/// SABR delivery with UMP demux (iOS SABRDelivery + LocalMediaServer).
/// Hosts localhost endpoints serving demuxed fMP4 video/audio buffers.
/// </summary>
public sealed class SabrDelivery : ISabrPlaybackController, IDisposable
{
    private HttpListener? _listener;
    private SabrSession? _session;
    private int _port;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private CancellationTokenSource? _cts;

    public bool IsSupported => true;
    public bool IsUmpImplemented => true;
    public string? ServerAbrStreamingUrl { get; private set; }
    public string? LocalVideoUrl { get; private set; }
    public string? LocalAudioUrl { get; private set; }
    public string? LastError { get; private set; }
    public bool IsActive => _session?.IsUmpActive == true;


    /// <summary>
    /// Start UMP session and local server. Returns local video URL when demux produces bytes.
    /// </summary>
    public async Task<string?> TryStartUmpAsync(
        WatchPage page,
        byte[]? poToken = null,
        CancellationToken ct = default)
    {
        ServerAbrStreamingUrl = page.ServerAbrStreamingUrl;
        if (string.IsNullOrEmpty(page.ServerAbrStreamingUrl))
        {
            LastError = "no serverAbrStreamingUrl";
            return null;
        }

        var ustreamerB64 = page.VideoPlaybackUstreamerConfig;
        if (string.IsNullOrEmpty(ustreamerB64))
        {
            LastError = "no videoPlaybackUstreamerConfig";
            return null;
        }

        byte[] ustreamer;
        try
        {
            // web-safe base64
            var s = ustreamerB64.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            ustreamer = Convert.FromBase64String(s);
        }
        catch (Exception ex)
        {
            LastError = "ustreamerConfig decode: " + ex.Message;
            return null;
        }

        var videoFmt = SelectVideoFormat(page);
        var audioFmt = SelectAudioFormat(page);
        if (videoFmt is null || audioFmt is null)
        {
            LastError = "missing adaptive itags for SABR";
            return null;
        }

        Stop();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _session = new SabrSession(
            _http,
            page.ServerAbrStreamingUrl!,
            ustreamer,
            videoFmt,
            audioFmt,
            poToken,
            SabrClientKind.Tv);

        try
        {
            await _session.StartAsync(_cts.Token).ConfigureAwait(false);
            await _session.PrefetchAsync(0, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = "SABR session: " + ex.Message;
            return null;
        }

        if (_session.VideoLength == 0)
        {
            LastError = _session.LastError ?? "UMP produced no video media";
            return null;
        }

        _session.StartPump();

        _port = await StartListenerAsync(_cts.Token).ConfigureAwait(false);
        LocalVideoUrl = $"http://127.0.0.1:{_port}/sabr/video.mp4";
        LocalAudioUrl = _session.AudioLength > 0
            ? $"http://127.0.0.1:{_port}/sabr/audio.mp4"
            : null;
        return LocalVideoUrl;
    }

    /// <summary>Legacy progressive proxy fallback.</summary>
    public async Task<string?> TryCreateLocalManifestAsync(
        string videoId,
        string? serverAbrUrl,
        string? hlsUrl,
        string? progressiveUrl,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(hlsUrl)) return hlsUrl;
        if (string.IsNullOrEmpty(progressiveUrl)) return serverAbrUrl;
        // simple pass-through — UMP path is TryStartUmpAsync
        return progressiveUrl;
    }

    private static SabrFormatInfo? SelectVideoFormat(WatchPage page)
    {
        var s = page.Streams
            .Where(x => x.IsVideoOnly || (x.Height is > 0 && !x.IsAudioOnly))
            .Where(x => x.Itag is > 0)
            .OrderByDescending(x => x.Height ?? 0)
            .ThenByDescending(x => x.Bitrate ?? 0)
            .FirstOrDefault();
        if (s is null) return null;
        return new SabrFormatInfo
        {
            Itag = s.Itag ?? 0,
            LastModified = s.LastModified,
            MimeType = s.MimeType,
            Bitrate = s.Bitrate,
            Width = s.Width,
            Height = s.Height
        };
    }

    private static SabrFormatInfo? SelectAudioFormat(WatchPage page)
    {
        var s = page.Streams
            .Where(x => x.IsAudioOnly)
            .Where(x => x.Itag is > 0)
            .OrderByDescending(x => x.Bitrate ?? 0)
            .FirstOrDefault();
        if (s is null) return null;
        return new SabrFormatInfo
        {
            Itag = s.Itag ?? 0,
            LastModified = s.LastModified,
            MimeType = s.MimeType,
            Bitrate = s.Bitrate
        };
    }

    private async Task<int> StartListenerAsync(CancellationToken ct)
    {
        Exception? last = null;
        for (var port = 18765; port < 18785; port++)
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
        throw new InvalidOperationException("SABR local server: no free port", last);
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
            var isAudio = path.Contains("audio", StringComparison.OrdinalIgnoreCase);
            var session = _session;
            if (session is null)
            {
                ctx.Response.StatusCode = 503;
                ctx.Response.Close();
                return;
            }

            var total = isAudio ? session.AudioLength : session.VideoLength;
            long start = 0, end = total - 1;
            var range = ctx.Request.Headers["Range"];
            if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var spec = range["bytes=".Length..];
                var parts = spec.Split('-');
                if (long.TryParse(parts[0], out var s)) start = s;
                if (parts.Length > 1 && long.TryParse(parts[1], out var e)) end = e;
                end = Math.Min(end, total - 1);
                ctx.Response.StatusCode = 206;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{total}";
            }
            else
            {
                ctx.Response.StatusCode = 200;
            }

            var length = (int)Math.Max(0, end - start + 1);
            ctx.Response.ContentType = "video/mp4";
            ctx.Response.ContentLength64 = length;
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            var buf = new byte[Math.Min(length, 64 * 1024)];
            var remaining = length;
            var offset = start;
            while (remaining > 0)
            {
                var n = isAudio
                    ? session.ReadAudio(offset, buf, Math.Min(buf.Length, remaining))
                    : session.ReadVideo(offset, buf, Math.Min(buf.Length, remaining));
                if (n <= 0) break;
                ctx.Response.OutputStream.Write(buf, 0, n);
                offset += n;
                remaining -= n;
            }
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* */ }
        }
    }

    public void StartPump() => _session?.StartPump();
    public void StopPump() => _session?.StopPump();

    public void ReportPlayerPosition(TimeSpan position)
        => _session?.ReportPlayerPosition(position);

    public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
        => _session is null
            ? Task.CompletedTask
            : _session.SeekAsync(position, ct);

    public void Stop()
    {
        try { _session?.StopPump(); } catch { /* */ }
        try { _cts?.Cancel(); } catch { /* */ }
        try { _listener?.Stop(); _listener?.Close(); } catch { /* */ }
        _listener = null;
        _session?.Dispose();
        _session = null;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
