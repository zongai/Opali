using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Opaline.Core.Models;

namespace Opaline.Core.Api;

/// <summary>
/// YouTube InnerTube API client.
/// Mirrors the iOS InnertubeClient architecture (Android / TV / Web client identities).
///
/// Note: YouTube frequently rotates client versions, signatures and PO tokens.
/// This implementation provides a working foundation; production use requires
/// keeping clientVersion / user-agent / visitor-data up to date (same as the iOS app).
/// </summary>
public sealed partial class InnertubeClient
{
    private readonly HttpClient _http;
    private readonly ClientIdentity _identity;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public InnertubeClient(HttpClient http, ClientIdentity? identity = null)
    {
        _http = http;
        _identity = identity ?? ClientIdentity.Android;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _identity.UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-YouTube-Client-Name", _identity.ClientNameId);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-YouTube-Client-Version", _identity.ClientVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    // ── Browse (Home / Subscriptions / Channel) ──────────────────────────

    public async Task<HomeFeed> GetHomeFeedAsync(string? continuation = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // iOS: anonymous → WEB browse; signed-in → TV authenticatedBrowse (device OAuth token)
        var signedIn = _oauth is not null && _oauth.IsSignedIn;
        var id = signedIn ? ClientIdentity.Tv : ClientIdentity.Web;
        var body = BuildContext(continuation is null
            ? new { browseId = "FEwhat_to_watch" }
            : new { continuation },
            id);

        var json = await PostAsync("browse", body, id, sendAuth: signedIn, ct).ConfigureAwait(false);
        var feed = ParseHomeFeed(json);
        feed = FilterLongForm(feed);
        System.Diagnostics.Debug.WriteLine(
            $"[Home] browse/{id.ClientName} auth={signedIn} items={feed.Items.Count} in {sw.ElapsedMilliseconds}ms");
        if (feed.Items.Count > 0 || continuation is not null)
            return feed;

        // Fast fallback: 2 searches in parallel (not 4 sequential)
        var merged = new List<FeedItem>();
        var seen = new HashSet<string>();
        var tasks = new[]
        {
            SearchAsync("recommended music videos", null, ct),
            SearchAsync("trending news today", null, ct)
        };
        var pages = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var page in pages)
        {
            foreach (var it in page.Items)
            {
                if (it is not VideoSearchItem v) continue;
                if (IsShortVideo(v.Video)) continue;
                if (!seen.Add(v.Video.Id)) continue;
                merged.Add(new VideoFeedItem { Id = v.Video.Id, Video = v.Video });
            }
        }
        System.Diagnostics.Debug.WriteLine($"[Home] fallback items={merged.Count} total {sw.ElapsedMilliseconds}ms");
        return new HomeFeed { Items = merged, ContinuationToken = null };
    }

    private static HomeFeed FilterLongForm(HomeFeed feed)
    {
        var items = feed.Items
            .Where(it => it is not VideoFeedItem v || !IsShortVideo(v.Video))
            .ToList();
        return new HomeFeed { Items = items, ContinuationToken = feed.ContinuationToken };
    }

    private static bool IsShortVideo(Video v)
    {
        if (v.IsShort) return true;
        if (v.Duration is { } d && d.TotalSeconds > 0 && d.TotalSeconds <= 60)
            return true;
        return false;
    }

    public async Task<HomeFeed> GetSubscriptionsFeedAsync(string? continuation = null, CancellationToken ct = default)
    {
        // Device-code OAuth is a TV token — use TVHTML5 + Bearer (WEB+Bearer → 400)
        var body = BuildContext(continuation is null
            ? new { browseId = "FEsubscriptions" }
            : new { continuation },
            ClientIdentity.Tv);

        var json = await PostAsync("browse", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
        return ParseHomeFeed(json);
    }

    // ── Player / Watch ───────────────────────────────────────────────────

    public async Task<WatchPage> GetWatchPageAsync(string videoId, CancellationToken ct = default)
    {
        var body = BuildContext(new
        {
            videoId,
            contentCheckOk = true,
            racyCheckOk = true
        }, ClientIdentity.Android);

        var json = await PostAsync("player", body, ClientIdentity.Android, sendAuth: false, ct).ConfigureAwait(false);
        return ParseWatchPage(json, videoId);
    }

    // ── Search ───────────────────────────────────────────────────────────

    public async Task<SearchPage> SearchAsync(string query, string? continuation = null, CancellationToken ct = default)
    {
        object payload = continuation is null
            ? new { query }
            : new { continuation };

        var body = BuildContext(payload, ClientIdentity.Web);
        var json = await PostAsync("search", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParseSearchPage(json);
    }

    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string query, CancellationToken ct = default)
    {
        // Public suggest endpoint (no auth required)
        var url = $"https://suggestqueries.google.com/complete/search?client=youtube&ds=yt&q={Uri.EscapeDataString(query)}";
        var text = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        // Response is JSONP-ish: window.google.ac.h([...])
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return Array.Empty<string>();

        var arr = JsonNode.Parse(text[start..(end + 1)]) as JsonArray;
        if (arr is null || arr.Count < 2) return Array.Empty<string>();

        var suggestions = new List<string>();
        if (arr[1] is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is JsonArray pair && pair.Count > 0 && pair[0] is JsonValue v)
                    suggestions.Add(v.GetValue<string>());
            }
        }
        return suggestions;
    }

    // ── Channel ──────────────────────────────────────────────────────────

    public async Task<Channel> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        var body = BuildContext(new { browseId = channelId }, ClientIdentity.Web);
        var json = await PostAsync("browse", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParseChannel(json, channelId);
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────

    private object BuildContext(object endpointPayload, ClientIdentity? identity = null)
    {
        var id = identity ?? _identity;
        object client = id.ClientName switch
        {
            "WEB" => new
            {
                clientName = "WEB",
                clientVersion = id.ClientVersion,
                hl = "en",
                gl = "US",
                platform = "DESKTOP",
                osName = "Windows",
                osVersion = "10.0",
                browserName = "Chrome",
                browserVersion = "140.0.0.0",
                userAgent = id.UserAgent,
                timeZone = "UTC",
                utcOffsetMinutes = 0
            },
            "TVHTML5" => new
            {
                clientName = "TVHTML5",
                clientVersion = id.ClientVersion,
                hl = "en",
                gl = "US",
                platform = "TV",
                timeZone = "UTC",
                utcOffsetMinutes = 0
            },
            _ => new
            {
                clientName = id.ClientName,
                clientVersion = id.ClientVersion,
                hl = "en",
                gl = "US",
                platform = "MOBILE",
                osName = "Android",
                osVersion = "14",
                androidSdkVersion = 34,
                timeZone = "UTC",
                utcOffsetMinutes = 0
            }
        };

        var dict = new Dictionary<string, object?>
        {
            ["context"] = new { client }
        };

        foreach (var prop in endpointPayload.GetType().GetProperties())
            dict[prop.Name] = prop.GetValue(endpointPayload);

        return dict;
    }


    // PostAsync lives in InnertubeClient.Auth.cs (auth-aware)


    // ── Parsers (simplified — production needs robust extraction like iOS) ─

    private static HomeFeed ParseHomeFeed(JsonNode json)
    {
        var items = new List<FeedItem>();
        TryExtractVideos(json, items);
        // Dedupe by video id (walk visits nested copies)
        var seen = new HashSet<string>();
        var unique = new List<FeedItem>();
        foreach (var it in items)
        {
            if (it is VideoFeedItem v)
            {
                if (!seen.Add(v.Video.Id)) continue;
            }
            unique.Add(it);
        }
        var continuation = FindContinuation(json);
        return new HomeFeed { Items = unique, ContinuationToken = continuation };
    }

    private static WatchPage ParseWatchPage(JsonNode json, string videoId)
    {
        var videoDetails = json["videoDetails"];
        var streamingData = json["streamingData"];

        var video = new Video
        {
            Id = videoId,
            Title = videoDetails?["title"]?.GetValue<string>() ?? "Unknown",
            Description = videoDetails?["shortDescription"]?.GetValue<string>(),
            ChannelId = videoDetails?["channelId"]?.GetValue<string>(),
            ChannelTitle = videoDetails?["author"]?.GetValue<string>(),
            ThumbnailUrl = videoDetails?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>(),
            ViewCount = long.TryParse(videoDetails?["viewCount"]?.GetValue<string>(), out var vc) ? vc : null,
            IsLive = videoDetails?["isLiveContent"]?.GetValue<bool>() ?? false,
            Duration = int.TryParse(videoDetails?["lengthSeconds"]?.GetValue<string>(), out var secs)
                ? TimeSpan.FromSeconds(secs) : null
        };

        var streams = new List<StreamInfo>();
        if (streamingData?["formats"] is JsonArray formats)
        {
            foreach (var f in formats)
            {
                streams.Add(ParseFormat(f));
            }
        }
        if (streamingData?["adaptiveFormats"] is JsonArray adaptive)
        {
            foreach (var f in adaptive)
            {
                streams.Add(ParseFormat(f));
            }
        }

        var hls = streamingData?["hlsManifestUrl"]?.GetValue<string>();
        var dash = streamingData?["dashManifestUrl"]?.GetValue<string>();

        var captions = ParseCaptionTracks(json);
        var videoQualities = streams
            .Where(s => !s.IsAudioOnly && (s.Height is > 0 || !string.IsNullOrEmpty(s.QualityLabel)))
            .GroupBy(s => s.Height ?? 0)
            .Select(g => g.OrderByDescending(s => s.Bitrate ?? 0).First())
            .OrderByDescending(s => s.Height ?? 0)
            .ToList();
        var audioTracks = streams
            .Where(s => s.IsAudioOnly)
            .OrderByDescending(s => s.Bitrate ?? 0)
            .ToList();

        return new WatchPage
        {
            Video = video,
            Streams = streams,
            HlsManifestUrl = hls,
            DashManifestUrl = dash,
            CaptionTracks = captions,
            VideoQualities = videoQualities,
            AudioTracks = audioTracks
        };
    }

    private static IReadOnlyList<CaptionTrack> ParseCaptionTracks(JsonNode json)
    {
        var list = new List<CaptionTrack>();
        var tracks = json["captions"]?["playerCaptionsTracklistRenderer"]?["captionTracks"] as JsonArray;
        if (tracks is null) return list;
        foreach (var t in tracks)
        {
            if (t is null) continue;
            var url = t["baseUrl"]?.GetValue<string>();
            if (string.IsNullOrEmpty(url)) continue;
            list.Add(new CaptionTrack
            {
                BaseUrl = url,
                LanguageCode = t["languageCode"]?.GetValue<string>() ?? "",
                LanguageName = t["name"]?["simpleText"]?.GetValue<string>()
                    ?? t["name"]?["runs"]?[0]?["text"]?.GetValue<string>()
                    ?? t["languageCode"]?.GetValue<string>()
                    ?? "Unknown",
                Kind = t["kind"]?.GetValue<string>(),
                IsTranslatable = t["isTranslatable"]?.GetValue<bool>() ?? false
            });
        }
        return list;
    }

    private static SearchPage ParseSearchPage(JsonNode json)
    {
        var items = new List<SearchItem>();
        // Simplified extraction
        TryExtractSearchItems(json, items);
        return new SearchPage
        {
            Items = items,
            ContinuationToken = FindContinuation(json)
        };
    }

    private static Channel ParseChannel(JsonNode json, string channelId)
    {
        var header = json["header"]?["c4TabbedHeaderRenderer"]
                  ?? json["header"]?["pageHeaderRenderer"];
        return new Channel
        {
            Id = channelId,
            Title = header?["title"]?.GetValue<string>()
                 ?? header?["pageTitle"]?.GetValue<string>()
                 ?? "Channel",
            AvatarUrl = header?["avatar"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>(),
            BannerUrl = header?["banner"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>(),
            SubscriberCount = null // needs deeper parse
        };
    }

    private static void TryExtractVideos(JsonNode node, List<FeedItem> items)
    {
        if (node is JsonObject obj)
        {
            // Classic renderers
            foreach (var key in new[] { "videoRenderer", "gridVideoRenderer", "compactVideoRenderer", "videoWithContextRenderer" })
            {
                if (obj.TryGetPropertyValue(key, out var vr) && vr is not null)
                {
                    var v = ParseVideoRenderer(vr);
                    if (v is not null)
                        items.Add(new VideoFeedItem { Id = v.Id, Video = v });
                }
            }

            // Modern lockup (WEB channel / some shelves)
            if (obj.TryGetPropertyValue("lockupViewModel", out var lockup) && lockup is not null)
            {
                var v = ParseLockupViewModel(lockup);
                if (v is not null)
                    items.Add(new VideoFeedItem { Id = v.Id, Video = v });
            }

            if (obj.TryGetPropertyValue("channelVideoPlayerRenderer", out var cvp) && cvp is not null)
            {
                var v = ParseVideoRenderer(cvp);
                if (v is not null)
                    items.Add(new VideoFeedItem { Id = v.Id, Video = v });
            }

            foreach (var kv in obj)
                if (kv.Value is not null)
                    TryExtractVideos(kv.Value, items);
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                if (child is not null)
                    TryExtractVideos(child, items);
        }
    }

    private static Video? ParseLockupViewModel(JsonNode lockup)
    {
        var contentType = lockup["contentType"]?.GetValue<string>() ?? "";
        if (contentType.Length > 0 && contentType is not "LOCKUP_CONTENT_TYPE_VIDEO")
            return null;

        var id = lockup["contentId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id) || id.Length is not 11)
            return null;

        var meta = lockup["metadata"]?["lockupMetadataViewModel"];
        var title = meta?["title"]?["content"]?.GetValue<string>() ?? "Untitled";

        // Optional secondary lines: channel / views
        string? channel = null;
        string? viewsText = null;
        if (meta?["metadata"]?["contentMetadataViewModel"]?["metadataRows"] is JsonArray rows)
        {
            foreach (var row in rows)
            {
                if (row?["metadataParts"] is not JsonArray parts) continue;
                foreach (var part in parts)
                {
                    var text = part?["text"]?["content"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(text)) continue;
                    if (text.Contains("view", StringComparison.OrdinalIgnoreCase) ||
                        text.Contains("watching", StringComparison.OrdinalIgnoreCase))
                        viewsText ??= text;
                    else
                        channel ??= text;
                }
            }
        }

        string? thumb = null;
        if (lockup["contentImage"]?["thumbnailViewModel"]?["image"]?["sources"] is JsonArray sources)
            thumb = sources.LastOrDefault()?["url"]?.GetValue<string>();

        // Duration from badge overlay
        TimeSpan? duration = null;
        if (lockup["contentImage"]?["thumbnailViewModel"]?["overlays"] is JsonArray overlays)
        {
            foreach (var ov in overlays)
            {
                var badge = ov?["thumbnailBottomOverlayViewModel"]?["badges"]?.AsArray()?.FirstOrDefault()
                    ?["thumbnailBadgeViewModel"]?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(badge) && badge.Contains(':'))
                {
                    var parts = badge.Split(':').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
                    duration = parts.Length switch
                    {
                        3 => new TimeSpan(parts[0], parts[1], parts[2]),
                        2 => new TimeSpan(0, parts[0], parts[1]),
                        _ => null
                    };
                    break;
                }
            }
        }

        return new Video
        {
            Id = id,
            Title = title,
            ChannelTitle = channel,
            ThumbnailUrl = thumb,
            Duration = duration,
            ViewCount = ParseViewCount(viewsText)
        };
    }

    private static void TryExtractSearchItems(JsonNode node, List<SearchItem> items)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in new[] { "videoRenderer", "gridVideoRenderer", "compactVideoRenderer" })
            {
                if (obj.TryGetPropertyValue(key, out var vr) && vr is not null)
                {
                    var v = ParseVideoRenderer(vr);
                    if (v is not null)
                        items.Add(new VideoSearchItem { Id = v.Id, Video = v });
                }
            }
            if (obj.TryGetPropertyValue("lockupViewModel", out var lockup) && lockup is not null)
            {
                var v = ParseLockupViewModel(lockup);
                if (v is not null)
                    items.Add(new VideoSearchItem { Id = v.Id, Video = v });
            }
            foreach (var kv in obj)
                if (kv.Value is not null)
                    TryExtractSearchItems(kv.Value, items);
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                if (child is not null)
                    TryExtractSearchItems(child, items);
        }
    }

    private static Video? ParseVideoRenderer(JsonNode vr)
    {
        var id = vr["videoId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        var titleRuns = vr["title"]?["runs"] as JsonArray;
        var title = titleRuns?.FirstOrDefault()?["text"]?.GetValue<string>()
                 ?? vr["title"]?["simpleText"]?.GetValue<string>()
                 ?? "Untitled";

        var lengthText = vr["lengthText"]?["simpleText"]?.GetValue<string>();
        TimeSpan? duration = null;
        if (!string.IsNullOrEmpty(lengthText))
        {
            var parts = lengthText.Split(':').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            duration = parts.Length switch
            {
                3 => new TimeSpan(parts[0], parts[1], parts[2]),
                2 => new TimeSpan(0, parts[0], parts[1]),
                _ => null
            };
        }

        return new Video
        {
            Id = id,
            Title = title,
            ChannelTitle = vr["ownerText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>()
                        ?? vr["shortBylineText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>(),
            ThumbnailUrl = vr["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>(),
            Duration = duration,
            ViewCount = ParseViewCount(vr["viewCountText"]?["simpleText"]?.GetValue<string>()
                                     ?? vr["viewCountText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>())
        };
    }

    private static long? ParseViewCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace(" views", "", StringComparison.OrdinalIgnoreCase)
                   .Replace(",", "")
                   .Trim();
        if (text.EndsWith("K", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^1], out var k)) return (long)(k * 1_000);
        if (text.EndsWith("M", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^1], out var m)) return (long)(m * 1_000_000);
        if (text.EndsWith("B", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^1], out var b)) return (long)(b * 1_000_000_000);
        return long.TryParse(text, out var n) ? n : null;
    }


    private static string? FindContinuation(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("continuationCommand", out var cmd)
                && cmd?["token"] is JsonValue token)
                return token.GetValue<string>();
            if (obj.TryGetPropertyValue("nextContinuationData", out var ncd)
                && ncd?["continuation"] is JsonValue cont)
                return cont.GetValue<string>();
            foreach (var kv in obj)
                if (kv.Value is not null)
                {
                    var found = FindContinuation(kv.Value);
                    if (found is not null) return found;
                }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                if (child is not null)
                {
                    var found = FindContinuation(child);
                    if (found is not null) return found;
                }
        }
        return null;
    }

    private static StreamInfo ParseFormat(JsonNode? f)
    {
        if (f is null)
            return new StreamInfo { Url = string.Empty, MimeType = "application/octet-stream" };

        string? url = f["url"]?.GetValue<string>();
        string? sigChallenge = null;
        string? sigParam = null;

        if (string.IsNullOrEmpty(url))
        {
            var cipher = f["signatureCipher"]?.GetValue<string>()
                      ?? f["cipher"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(cipher))
                (url, sigChallenge, sigParam) = ParseCipher(cipher!);
        }

        var mime = f["mimeType"]?.GetValue<string>() ?? "video/mp4";
        var isAudio = mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        var isVideoOnly = mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                          && f["audioQuality"] is null
                          && f["audioSampleRate"] is null;

        string? nParam = null;
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var q = new Uri(url!).Query;
                foreach (var part in q.TrimStart('?').Split('&'))
                {
                    if (part.StartsWith("n=", StringComparison.OrdinalIgnoreCase))
                    {
                        nParam = Uri.UnescapeDataString(part[2..]);
                        break;
                    }
                }
            }
            catch { /* ignore */ }
        }

        return new StreamInfo
        {
            Url = url ?? string.Empty,
            MimeType = mime,
            Width = f["width"]?.GetValue<int>(),
            Height = f["height"]?.GetValue<int>(),
            Fps = f["fps"]?.GetValue<int>(),
            Bitrate = f["bitrate"]?.GetValue<long>(),
            QualityLabel = f["qualityLabel"]?.GetValue<string>(),
            IsAudioOnly = isAudio,
            IsVideoOnly = isVideoOnly,
            Codecs = mime,
            ContentLength = long.TryParse(f["contentLength"]?.GetValue<string>(), out var cl) ? cl : null,
            Itag = f["itag"]?.GetValue<int>(),
            SigChallenge = sigChallenge,
            SigParam = sigParam,
            NParam = nParam
        };
    }

    /// <summary>
    /// Parse signatureCipher: url=...&amp;s=...&amp;sp=sig
    /// Does NOT append unsolved s — StreamUrlResolver solves it first.
    /// </summary>
    private static (string? Url, string? S, string? Sp) ParseCipher(string cipher)
    {
        string? url = null, s = null, sp = null;
        foreach (var part in cipher.Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            var val = Uri.UnescapeDataString(part[(eq + 1)..]);
            switch (key)
            {
                case "url": url = val; break;
                case "s": s = val; break;
                case "sp": sp = val; break;
            }
        }
        return (url, s, sp ?? "sig");
    }
}

/// <summary>
/// Client identity used for InnerTube requests (Android recommended for stream URLs).
/// </summary>
public sealed class ClientIdentity
{
    public string ClientName { get; init; }
    public string ClientNameId { get; init; }
    public string ClientVersion { get; init; }
    public string UserAgent { get; init; }
    public string ApiKey { get; init; }

    public static ClientIdentity Android { get; } = new()
    {
        ClientName = "ANDROID",
        ClientNameId = "3",
        ClientVersion = "21.26.364",
        UserAgent = "com.google.android.youtube/21.26.364 (Linux; U; Android 14) gzip",
        ApiKey = "AIzaSyA8eiZmM1FaDVjRy-df2KTyQ_vz_yYM39w"
    };

    public static ClientIdentity Web { get; } = new()
    {
        ClientName = "WEB",
        ClientNameId = "1",
        ClientVersion = "2.20260206.01.00",
        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36",
        ApiKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8"
    };

    /// <summary>TV client — matches device-code OAuth tokens (bearer works here).</summary>
    public static ClientIdentity Tv { get; } = new()
    {
        ClientName = "TVHTML5",
        ClientNameId = "7",
        ClientVersion = "7.20260311.12.00",
        UserAgent = "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version",
        ApiKey = "AIzaSyA8eiZmM1FaDVjRy-df2KTyQ_vz_yYM39w"
    };
}
