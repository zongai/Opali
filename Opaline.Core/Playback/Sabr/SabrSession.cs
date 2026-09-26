using System.Net.Http.Headers;

namespace Opaline.Core.Playback.Sabr;

/// <summary>
/// SABR session: TV abrState, playback cookie, continuous segment pump, seek.
/// </summary>
public sealed class SabrSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly byte[] _ustreamerConfig;
    private readonly SabrFormatInfo _video;
    private readonly SabrFormatInfo _audio;
    private readonly SabrClientKind _clientKind;
    private readonly byte[]? _poToken;
    private string _url;
    private byte[]? _playbackCookie;
    private int _backoffMs;
    private int _requestNumber;
    private SabrBufferedRange? _heldVideo;
    private SabrBufferedRange? _heldAudio;
    private readonly Dictionary<int, int> _lastRequestedMs = new();
    private readonly MemoryStream _videoBuffer = new();
    private readonly MemoryStream _audioBuffer = new();
    private readonly object _lock = new();
    private bool _started;
    private int _playerMs;
    private int _bufferedUntilVideoMs;
    private int _bufferedUntilAudioMs;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    /// <summary>How far ahead of the playhead to keep buffered (ms).</summary>
    public int TargetLeadMs { get; set; } = 18_000;

    public SabrSession(
        HttpClient http,
        string serverAbrUrl,
        byte[] ustreamerConfig,
        SabrFormatInfo video,
        SabrFormatInfo audio,
        byte[]? poToken = null,
        SabrClientKind clientKind = SabrClientKind.Tv)
    {
        _http = http;
        _url = serverAbrUrl;
        _ustreamerConfig = ustreamerConfig;
        _video = video;
        _audio = audio;
        _poToken = poToken;
        _clientKind = clientKind;
    }

    public long VideoLength { get { lock (_lock) return _videoBuffer.Length; } }
    public long AudioLength { get { lock (_lock) return _audioBuffer.Length; } }
    public bool IsUmpActive => _started;
    public string? LastError { get; private set; }
    public int CookieBytes => _playbackCookie?.Length ?? 0;
    public SabrClientKind ClientKind => _clientKind;
    public int PlayerMs { get { lock (_lock) return _playerMs; } }
    public int BufferedUntilVideoMs { get { lock (_lock) return _bufferedUntilVideoMs; } }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return;
        var body = SabrRequestBuilder.Startup(
            _ustreamerConfig, _audio, _video, _clientKind, _poToken, _playbackCookie);
        var collector = await PostAsync(body, ct).ConfigureAwait(false);
        AppendMedia(collector);
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

        for (var i = 0; i < 6; i++)
        {
            await RespectBackoffAsync(ct).ConfigureAwait(false);
            await FetchSegmentAsync(_video, _audio, isInit: false, playerMs: targetPlayerMs, ct).ConfigureAwait(false);
            await FetchSegmentAsync(_audio, _video, isInit: false, playerMs: targetPlayerMs, ct).ConfigureAwait(false);
            targetPlayerMs += 2000;
            if (VideoLength > 2_000_000 && AudioLength > 200_000)
                break;
        }
    }

    /// <summary>Start continuous pull so buffer stays TargetLeadMs ahead of playhead.</summary>
    public void StartPump()
    {
        StopPump();
        _pumpCts = new CancellationTokenSource();
        var ct = _pumpCts.Token;
        _pumpTask = Task.Run(() => PumpLoopAsync(ct), ct);
    }

    public void StopPump()
    {
        try { _pumpCts?.Cancel(); } catch { /* */ }
        _pumpCts = null;
        _pumpTask = null;
    }

    /// <summary>Called by player clock (~500ms).</summary>
    public void ReportPlayerPosition(TimeSpan position)
    {
        lock (_lock)
            _playerMs = (int)Math.Max(0, position.TotalMilliseconds);
    }

    /// <summary>
    /// Precise seek: update playhead, drop held if rewinding, pull segments at target.
    /// </summary>
    public async Task SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        var ms = (int)Math.Max(0, position.TotalMilliseconds);
        lock (_lock)
        {
            var prev = _playerMs;
            _playerMs = ms;
            if (ms < prev)
            {
                _heldVideo = null;
                _heldAudio = null;
                // Keep byte buffers (init segments); server will re-serve media at new time
            }
            // Invalidate "ahead" bookkeeping so pump fills from seek point
            if (ms + 500 < _bufferedUntilVideoMs)
                _bufferedUntilVideoMs = ms;
            if (ms + 500 < _bufferedUntilAudioMs)
                _bufferedUntilAudioMs = ms;
        }

        await RespectBackoffAsync(ct).ConfigureAwait(false);
        // Burst a few segments at seek target
        for (var i = 0; i < 4; i++)
        {
            await FetchSegmentAsync(_video, _audio, isInit: false, playerMs: ms + i * 2000, ct).ConfigureAwait(false);
            await FetchSegmentAsync(_audio, _video, isInit: false, playerMs: ms + i * 2000, ct).ConfigureAwait(false);
        }
    }

    private async Task PumpLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int playerMs, bufV, bufA, lead;
                lock (_lock)
                {
                    playerMs = _playerMs;
                    bufV = _bufferedUntilVideoMs;
                    bufA = _bufferedUntilAudioMs;
                    lead = TargetLeadMs;
                }

                var needVideo = bufV < playerMs + lead;
                var needAudio = bufA < playerMs + lead;

                if (needVideo || needAudio)
                {
                    await RespectBackoffAsync(ct).ConfigureAwait(false);
                    var t = Math.Max(playerMs, Math.Min(bufV, bufA));
                    if (needVideo)
                        await FetchSegmentAsync(_video, _audio, isInit: false, playerMs: t, ct).ConfigureAwait(false);
                    if (needAudio)
                        await FetchSegmentAsync(_audio, _video, isInit: false, playerMs: t, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(400, ct).ConfigureAwait(false);
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch { break; }
            }
        }
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

    private async Task RespectBackoffAsync(CancellationToken ct)
    {
        int ms;
        lock (_lock) ms = _backoffMs;
        if (ms > 0)
        {
            await Task.Delay(Math.Min(ms, 5000), ct).ConfigureAwait(false);
            lock (_lock) _backoffMs = 0;
        }
    }

    private async Task FetchSegmentAsync(
        SabrFormatInfo format, SabrFormatInfo other, bool isInit, int playerMs, CancellationToken ct)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var itag = format.Itag;
            SabrBufferedRange? held;
            byte[]? cookie;
            lock (_lock)
            {
                if (_lastRequestedMs.TryGetValue(itag, out var last) && playerMs < last)
                {
                    if ((format.Height ?? 0) > 0) _heldVideo = null;
                    else _heldAudio = null;
                }
                _lastRequestedMs[itag] = playerMs;
                held = (format.Height ?? 0) > 0 ? _heldVideo : _heldAudio;
                cookie = _playbackCookie;
            }

            var body = SabrRequestBuilder.Segment(
                _ustreamerConfig, format, other, isInit, playerMs, held, _clientKind, _poToken, cookie);
            var collector = await PostAsync(body, ct).ConfigureAwait(false);
            AppendMedia(collector);
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    private async Task<SabrSegmentCollector> PostAsync(byte[] body, CancellationToken ct)
    {
        int rn;
        string url;
        lock (_lock)
        {
            _requestNumber++;
            rn = _requestNumber;
            url = _url.Contains('?') ? $"{_url}&rn={rn}" : $"{_url}?rn={rn}";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new ByteArrayContent(body);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        var ua = _clientKind == SabrClientKind.Tv
            ? "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version"
            : "com.google.android.youtube/21.26.364 (Linux; U; Android 14) gzip";
        req.Headers.TryAddWithoutValidation("User-Agent", ua);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var collector = new SabrSegmentCollector();
        collector.Append(bytes);
        collector.CompleteIfHasMedia();

        ApplyPolicy(collector.Policy);

        if (!string.IsNullOrEmpty(collector.RedirectUrl))
        {
            lock (_lock) _url = collector.RedirectUrl!;
        }
        if (!string.IsNullOrEmpty(collector.ErrorDetail))
            LastError = collector.ErrorDetail;

        if (collector.DeliveredRange is { } range)
        {
            lock (_lock)
            {
                if (range.Format.Itag == _video.Itag || (range.Format.Height ?? 0) > 0)
                    _heldVideo = range;
                else
                    _heldAudio = range;
            }
        }

        return collector;
    }

    private void ApplyPolicy(SabrPolicy? policy)
    {
        if (policy is null) return;
        lock (_lock)
        {
            if (policy.PlaybackCookie is { Length: > 0 })
                _playbackCookie = policy.PlaybackCookie;
            if (policy.BackoffMs > 0)
                _backoffMs = policy.BackoffMs;
        }
    }

    private void AppendMedia(SabrSegmentCollector collector)
    {
        var seg = collector.Segment;
        if (seg is null || seg.Length == 0) return;
        var header = collector.Header;
        var isVideo = true;
        if (header is not null)
        {
            if (header.Itag == _audio.Itag) isVideo = false;
            else if (header.Itag == _video.Itag) isVideo = true;
            else isVideo = header.Itag != _audio.Itag;
        }

        lock (_lock)
        {
            if (isVideo)
            {
                _videoBuffer.Position = _videoBuffer.Length;
                _videoBuffer.Write(seg, 0, seg.Length);
                if (header is not null)
                {
                    var end = header.StartMs + header.DurationMs;
                    if (end > _bufferedUntilVideoMs)
                        _bufferedUntilVideoMs = end;
                }
            }
            else
            {
                _audioBuffer.Position = _audioBuffer.Length;
                _audioBuffer.Write(seg, 0, seg.Length);
                if (header is not null)
                {
                    var end = header.StartMs + header.DurationMs;
                    if (end > _bufferedUntilAudioMs)
                        _bufferedUntilAudioMs = end;
                }
            }
        }
    }

    public void Dispose()
    {
        StopPump();
        _fetchGate.Dispose();
        _videoBuffer.Dispose();
        _audioBuffer.Dispose();
    }
}
