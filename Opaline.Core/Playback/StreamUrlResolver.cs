using Opaline.Core.Playback.Hls;
using Opaline.Core.Models;

namespace Opaline.Core.Playback;

/// <summary>
/// Resolves playable media URLs with full signature pipeline:
/// HLS/DASH manifests → progressive → adaptive pair (video+audio),
/// applying <c>s</c> (signatureCipher) and <c>n</c> transforms via remote solver.
/// </summary>
public sealed class StreamUrlResolver
{
    private readonly SignatureSolverService _solver;
    private readonly PoTokenService _poToken;
    private readonly SignatureTimestampService _sts;
    private readonly SabrDelivery _sabr = new();
    private readonly HlsSelfBuiltDelivery _hlsSelf = new();

    public StreamUrlResolver(
        SignatureSolverService solver,
        PoTokenService poToken,
        SignatureTimestampService sts)
    {
        _solver = solver;
        _poToken = poToken;
        _sts = sts;
    }

    public async Task<ResolvedStream?> ResolveAsync(
        WatchPage page,
        int maxHeight = 1080,
        bool preferAdaptive = false,
        CancellationToken ct = default)
    {
        _ = await _sts.GetAsync(ct).ConfigureAwait(false);
        await _solver.EnsurePlayerJsAsync(page.Video.Id, ct).ConfigureAwait(false);

        // SABR UMP demux → localhost fMP4 (when serverAbr + ustreamer present)
        if (!string.IsNullOrEmpty(page.ServerAbrStreamingUrl)
            && (!string.IsNullOrEmpty(page.VideoPlaybackUstreamerConfig)
                || !string.IsNullOrEmpty(page.OnesieUstreamerConfig)))
        {
            try
            {
                var pot = await _poToken.FetchAsync(page.Video.Id, "ANDROID", ct).ConfigureAwait(false);
                byte[]? potBytes = null;
                if (!string.IsNullOrEmpty(pot))
                {
                    try
                    {
                        var s = pot!.Replace('-', '+').Replace('_', '/');
                        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                        potBytes = Convert.FromBase64String(s);
                    }
                    catch { /* ignore pot decode */ }
                }
                var local = await _sabr.TryStartUmpAsync(page, potBytes, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(local))
                {
                    return new ResolvedStream(
                        PrimaryUrl: local!,
                        Video: new StreamInfo { Url = local!, MimeType = "video/mp4", QualityLabel = string.IsNullOrEmpty(_sabr.UstreamerSource) ? "SABR-UMP" : ("SABR-" + _sabr.UstreamerSource) },
                        Audio: string.IsNullOrEmpty(_sabr.LocalAudioUrl) ? null : new StreamInfo
                        {
                            Url = _sabr.LocalAudioUrl!,
                            MimeType = "audio/mp4",
                            IsAudioOnly = true,
                            QualityLabel = "SABR-audio"
                        },
                        IsProgressive: _sabr.LocalAudioUrl is null,
                        Kind: _sabr.LocalAudioUrl is null ? StreamKind.Progressive : StreamKind.AdaptivePair,
                        Sabr: _sabr);
                }
            }
            catch
            {
                // fall through to HLS / progressive
            }
        }


        // 1) Prefer server HLS / DASH manifests (native AdaptiveMediaSource on Windows)
        if (!string.IsNullOrEmpty(page.HlsManifestUrl))
        {
            var hls = await FinalizeUrlAsync(page.HlsManifestUrl!, page.Video.Id, null, null, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(hls))
            {
                return new ResolvedStream(
                    PrimaryUrl: hls!,
                    Video: new StreamInfo { Url = hls!, MimeType = "application/vnd.apple.mpegurl", QualityLabel = "HLS" },
                    Audio: null,
                    IsProgressive: false,
                    Kind: StreamKind.HlsManifest);
            }
        }
        if (!string.IsNullOrEmpty(page.DashManifestUrl))
        {
            var dash = await FinalizeUrlAsync(page.DashManifestUrl!, page.Video.Id, null, null, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(dash))
            {
                return new ResolvedStream(
                    PrimaryUrl: dash!,
                    Video: new StreamInfo { Url = dash!, MimeType = "application/dash+xml", QualityLabel = "DASH" },
                    Audio: null,
                    IsProgressive: false,
                    Kind: StreamKind.DashManifest);
            }
        }


        // Self-built HLS from SIDX (iOS HLSPlaybackBuilder) — before progressive dual
        {
            var (vFmt, aFmt) = SelectBestAdaptive(page, maxHeight);
            if (vFmt is not null && aFmt is not null
                && vFmt.IndexRangeEnd > 0 && aFmt.IndexRangeEnd > 0)
            {
                try
                {
                    var vUrl = await FinalizeUrlAsync(
                        vFmt.Url, page.Video.Id, vFmt.SigChallenge, vFmt.SigParam, ct).ConfigureAwait(false);
                    var aUrl = await FinalizeUrlAsync(
                        aFmt.Url, page.Video.Id, aFmt.SigChallenge, aFmt.SigParam, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(vUrl) && !string.IsNullOrEmpty(aUrl))
                    {
                        var local = await _hlsSelf.TryBuildAsync(vFmt, aFmt, vUrl, aUrl, ct)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(local))
                        {
                            return new ResolvedStream(
                                PrimaryUrl: local!,
                                Video: new StreamInfo
                                {
                                    Url = local!,
                                    MimeType = "application/vnd.apple.mpegurl",
                                    QualityLabel = "HLS-SIDX",
                                    Height = vFmt.Height,
                                    Width = vFmt.Width
                                },
                                Audio: null,
                                IsProgressive: false,
                                Kind: StreamKind.HlsManifest);
                        }
                    }
                }
                catch
                {
                    // fall through
                }
            }
        }

        // 2) Progressive combined A+V
        if (!preferAdaptive)
        {
            var progressive = SelectBestProgressive(page, maxHeight);
            if (progressive is not null)
            {
                var url = await FinalizeUrlAsync(
                    progressive.Url, page.Video.Id,
                    progressive.SigChallenge, progressive.SigParam, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(url))
                    return new ResolvedStream(url!, progressive, null, IsProgressive: true, StreamKind.Progressive);
            }
        }

        // 3) Adaptive pair — separate video + audio (dual MediaPlayer on UI side)
        var (video, audio) = SelectBestAdaptive(page, maxHeight);
        if (video is null) return null;

        var videoUrl = await FinalizeUrlAsync(
            video.Url, page.Video.Id, video.SigChallenge, video.SigParam, ct).ConfigureAwait(false);
        string? audioUrl = null;
        if (audio is not null)
        {
            audioUrl = await FinalizeUrlAsync(
                audio.Url, page.Video.Id, audio.SigChallenge, audio.SigParam, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(videoUrl)) return null;

        StreamInfo? audioInfo = null;
        if (audio is not null && !string.IsNullOrEmpty(audioUrl))
        {
            audioInfo = new StreamInfo
            {
                Url = audioUrl!,
                MimeType = audio.MimeType,
                Bitrate = audio.Bitrate,
                IsAudioOnly = true,
                QualityLabel = audio.QualityLabel,
                Codecs = audio.Codecs
            };
        }

        return new ResolvedStream(
            videoUrl!,
            new StreamInfo
            {
                Url = videoUrl!,
                MimeType = video.MimeType,
                Width = video.Width,
                Height = video.Height,
                Fps = video.Fps,
                Bitrate = video.Bitrate,
                QualityLabel = video.QualityLabel,
                IsVideoOnly = true,
                Codecs = video.Codecs
            },
            audioInfo,
            IsProgressive: false,
            audioInfo is null ? StreamKind.VideoOnly : StreamKind.AdaptivePair);
    }

    /// <summary>
    /// Apply s-signature + n-transform + optional pot to a media URL.
    /// </summary>
    public async Task<string?> FinalizeUrlAsync(
        string rawUrl,
        string videoId,
        string? sigChallenge,
        string? sigParam,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawUrl)) return null;

        try
        {
            var uri = new Uri(rawUrl);
            var query = ParseQuery(uri.Query);

            // Collect challenges
            var n = query.TryGetValue("n", out var nVal) ? nVal : null;
            var s = sigChallenge;
            // Some cipher URLs already embed a placeholder sig param
            if (s is null && !string.IsNullOrEmpty(sigParam) && query.TryGetValue(sigParam, out var existingSig))
                s = existingSig;

            var (solvedN, solvedS) = await _solver.SolveBothAsync(n, s, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(solvedN))
                query["n"] = solvedN!;

            if (!string.IsNullOrEmpty(solvedS))
            {
                var paramName = string.IsNullOrEmpty(sigParam) ? "sig" : sigParam;
                query[paramName] = solvedS!;
            }

            // pot is optional but helps some clients
            // pot binds to client name (iOS); try WEB then ANDROID remote mint
            if (!query.ContainsKey("pot"))
            {
                var pot = await _poToken.FetchAsync(videoId, "WEB", ct).ConfigureAwait(false)
                       ?? await _poToken.FetchAsync(videoId, "ANDROID", ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(pot))
                    query["pot"] = pot!;
            }

            var q = string.Join("&", query.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            return new UriBuilder(uri) { Query = q }.Uri.ToString();
        }
        catch
        {
            return rawUrl;
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return dict;
        var q = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0)
                dict[Uri.UnescapeDataString(part)] = string.Empty;
            else
                dict[Uri.UnescapeDataString(part[..idx])] =
                    Uri.UnescapeDataString(part[(idx + 1)..]);
        }
        return dict;
    }

    public static StreamInfo? SelectBestProgressive(WatchPage page, int maxHeight = 1080)
        => page.Streams
            .Where(s => !s.IsAudioOnly && !s.IsVideoOnly && !string.IsNullOrEmpty(s.Url))
            .Where(s => s.Height is null || s.Height <= maxHeight)
            .OrderByDescending(s => s.Height ?? 0)
            .ThenByDescending(s => s.Bitrate ?? 0)
            .FirstOrDefault();

    public static (StreamInfo? Video, StreamInfo? Audio) SelectBestAdaptive(
        WatchPage page, int maxHeight = 1080)
    {
        // Video ladder: admit av01 only when Av1Support allows (iOS AV1Support)
        var videoCandidates = page.Streams
            .Where(s => s.IsVideoOnly && !string.IsNullOrEmpty(s.Url))
            .Where(s => s.Height is null || s.Height <= maxHeight)
            .Where(s => Av1Support.AllowsMime(s.MimeType) && Av1Support.AllowsMime(s.Codecs))
            .ToList();

        // Prefer AV1 when supported, else prefer avc1 over vp9 for broader decode
        var video = videoCandidates
            .OrderByDescending(s => s.Height ?? 0)
            .ThenByDescending(s => ScoreVideoCodec(s))
            .ThenByDescending(s => s.Bitrate ?? 0)
            .FirstOrDefault();

        var audioList = page.Streams
            .Where(s => s.IsAudioOnly && !string.IsNullOrEmpty(s.Url))
            .ToList();
        var audio = AutoDubPreference.SelectAudio(audioList)
            ?? audioList
                .OrderByDescending(s => s.MimeType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(s => s.Bitrate ?? 0)
                .FirstOrDefault();

        return (video, audio);
    }

    private static int ScoreVideoCodec(StreamInfo s)
    {
        var m = ((s.MimeType ?? "") + (s.Codecs ?? "")).ToLowerInvariant();
        if (Av1Support.IsSupported && m.Contains("av01")) return 3;
        if (m.Contains("avc1") || m.Contains("avc")) return 2;
        if (m.Contains("vp9") || m.Contains("vp09")) return 1;
        return 0;
    }
}

public enum StreamKind
{
    Progressive,
    HlsManifest,
    DashManifest,
    AdaptivePair,
    VideoOnly
}

public sealed record ResolvedStream(
    string PrimaryUrl,
    StreamInfo Video,
    StreamInfo? Audio,
    bool IsProgressive,
    StreamKind Kind,
    ISabrPlaybackController? Sabr = null);
