using System.Net.Http.Headers;

namespace Opaline.Core.Playback.Sabr;

/// <summary>
/// One SABR playback session: startup + sequential segment fetches with UMP demux.
/// Accumulates init + media into a continuous fMP4 buffer for local serving.
/// </summary>
public sealed class SabrSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _serverAbrUrl;
    private readonly byte[] _ustreamerConfig;
    private readonly SabrFormatInfo _video;
    private readonly SabrFormatInfo _audio;
    private readonly byte[] _clientInfo;
    private readonly byte[]? _poToken;
    private string _url;
    private byte[]? _playbackCookie;
    private SabrBufferedRange? _heldVideo;
    private SabrBufferedRange? _heldAudio;
    private readonly MemoryStream _videoBuffer = new();
    private readonly MemoryStream _audioBuffer = new();
    private readonly object _lock = new();
    private bool _started;

    public SabrSession(
        HttpClient http,
        string serverAbrUrl,
        byte[] ustreamerConfig,
        SabrFormatInfo video,
        SabrFormatInfo audio,
        byte[]? poToken = null)
    {
        _http = http;
        _serverAbrUrl = serverAbrUrl;
        _url = serverAbrUrl;
        _ustreamerConfig = ustreamerConfig;
        _video = video;
        _audio = audio;
        _clientInfo = SabrRequestBuilder.ClientInfoAndroid();
        _poToken = poToken;
    }

    public long VideoLength { get { lock (_lock) return _videoBuffer.Length; } }
    public long AudioLength { get { lock (_lock) return _audioBuffer.Length; } }
    public bool IsUmpActive => _started;
    public string? LastError { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;
        var body = SabrRequestBuilder.Startup(
            _ustreamerConfig, _audio, _video, _clientInfo, _poToken, _playbackCookie);
        var collector = await PostAsync(body, ct).ConfigureAwait(false);
        AppendMedia(collector);
        // Follow with explicit init for video then audio if startup did not fill
        if (VideoLength == 0)
            await FetchSegmentAsync(_video, _audio, isInit: true, playerMs: 0, ct).ConfigureAwait(false);
        if (AudioLength == 0)
            await FetchSegmentAsync(_audio, _video, isInit: true, playerMs: 0, ct).ConfigureAwait(false);
        _started = true;
    }

    public async Task PrefetchAsync(int targetPlayerMs, CancellationToken ct = default)
    {
        if (!_started)
            await StartAsync(ct).ConfigureAwait(false);

        // Pull a few media segments for each track
        for (var i = 0; i < 6; i++)
        {
            await FetchSegmentAsync(_video, _audio, isInit: false, playerMs: targetPlayerMs, ct).ConfigureAwait(false);
            await FetchSegmentAsync(_audio, _video, isInit: false, playerMs: targetPlayerMs, ct).ConfigureAwait(false);
            targetPlayerMs += 2000;
            if (VideoLength > 2_000_000 && AudioLength > 200_000)
                break;
        }
    }

    public byte[] SnapshotVideo()
    {
        lock (_lock) return _videoBuffer.ToArray();
    }

    public byte[] SnapshotAudio()
    {
        lock (_lock) return _audioBuffer.ToArray();
    }

    public int ReadVideo(long offset, byte[] buffer, int count)
    {
        lock (_lock)
        {
            if (offset >= _videoBuffer.Length) return 0;
            _videoBuffer.Position = offset;
            return _videoBuffer.Read(buffer, 0, (int)Math.Min(count, _videoBuffer.Length - offset));
        }
    }

    public int ReadAudio(long offset, byte[] buffer, int count)
    {
        lock (_lock)
        {
            if (offset >= _audioBuffer.Length) return 0;
            _audioBuffer.Position = offset;
            return _audioBuffer.Read(buffer, 0, (int)Math.Min(count, _audioBuffer.Length - offset));
        }
    }

    private async Task FetchSegmentAsync(
        SabrFormatInfo format, SabrFormatInfo other, bool isInit, int playerMs, CancellationToken ct)
    {
        var held = (format.Height ?? 0) > 0 ? _heldVideo : _heldAudio;
        var body = SabrRequestBuilder.Segment(
            _ustreamerConfig, format, other, isInit, playerMs, held, _clientInfo, _poToken, _playbackCookie);
        var collector = await PostAsync(body, ct).ConfigureAwait(false);
        AppendMedia(collector);
    }

    private async Task<SabrSegmentCollector> PostAsync(byte[] body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _url);
        req.Content = new ByteArrayContent(body);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        req.Headers.TryAddWithoutValidation("User-Agent",
            "com.google.android.youtube/21.26.364 (Linux; U; Android 14) gzip");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var collector = new SabrSegmentCollector();
        collector.Append(bytes);
        collector.CompleteIfHasMedia();

        if (!string.IsNullOrEmpty(collector.RedirectUrl))
            _url = collector.RedirectUrl!;
        if (!string.IsNullOrEmpty(collector.ErrorDetail))
            LastError = collector.ErrorDetail;

        // Extract playback cookie from nextRequestPolicy if present (field 1)
        // skipped — optional for first segments

        if (collector.DeliveredRange is { } range)
        {
            if ((range.Format.Itag == _video.Itag) || (range.Format.Height ?? 0) > 0)
                _heldVideo = range;
            else
                _heldAudio = range;
        }

        return collector;
    }

    private void AppendMedia(SabrSegmentCollector collector)
    {
        var seg = collector.Segment;
        if (seg is null || seg.Length == 0) return;
        var header = collector.Header;
        var isVideo = header is null
            || header.Itag == _video.Itag
            || (header.Itag != _audio.Itag && (header.Itag > 100 || (_video.Height ?? 0) > 0));

        // Prefer itag match
        if (header is not null)
        {
            if (header.Itag == _audio.Itag) isVideo = false;
            else if (header.Itag == _video.Itag) isVideo = true;
        }

        lock (_lock)
        {
            if (isVideo)
            {
                _videoBuffer.Position = _videoBuffer.Length;
                _videoBuffer.Write(seg, 0, seg.Length);
            }
            else
            {
                _audioBuffer.Position = _audioBuffer.Length;
                _audioBuffer.Write(seg, 0, seg.Length);
            }
        }
    }

    public void Dispose()
    {
        _videoBuffer.Dispose();
        _audioBuffer.Dispose();
    }
}
