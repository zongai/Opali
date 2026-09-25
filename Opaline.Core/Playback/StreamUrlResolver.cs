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
                    return new ResolvedStream(url!, progressive, null, isProgressive: true, StreamKind.Progressive);
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
            isProgressive: false,
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
            var pot = await _poToken.FetchAsync(videoId, "WEB", ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pot) && !query.ContainsKey("pot"))
                query["pot"] = pot!;

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
            .Where(s => s.Height is null or <= maxHeight)
            .OrderByDescending(s => s.Height ?? 0)
            .ThenByDescending(s => s.Bitrate ?? 0)
            .FirstOrDefault();

    public static (StreamInfo? Video, StreamInfo? Audio) SelectBestAdaptive(
        WatchPage page, int maxHeight = 1080)
    {
        var video = page.Streams
            .Where(s => s.IsVideoOnly && !string.IsNullOrEmpty(s.Url))
            .Where(s => s.Height is null or <= maxHeight)
            .OrderByDescending(s => s.Height ?? 0)
            .ThenByDescending(s => s.Bitrate ?? 0)
            .FirstOrDefault();

        // Prefer mp4/webm audio, highest bitrate
        var audio = page.Streams
            .Where(s => s.IsAudioOnly && !string.IsNullOrEmpty(s.Url))
            .OrderByDescending(s => s.MimeType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(s => s.Bitrate ?? 0)
            .FirstOrDefault();

        return (video, audio);
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
    StreamKind Kind);
