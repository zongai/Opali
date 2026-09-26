using System.Text.Json.Nodes;
using Opaline.Core.Models;

namespace Opaline.Core.Api;

public sealed partial class InnertubeClient
{
    // ── Channel videos ───────────────────────────────────────────────────

    public async Task<ChannelPage> GetChannelPageAsync(string channelId, string? continuation = null, CancellationToken ct = default)
    {
        object body;
        if (continuation is null)
        {
            // Videos tab params (InnerTube encoded): common for channel videos
            body = BuildContext(new
            {
                browseId = channelId,
                params_ = "EgZ2aWRlb3PyBgQKAjoA" // will be fixed below
            }, ClientIdentity.Web);
            // anonymous object can't use params as property easily — rebuild
            body = BuildChannelBrowseBody(channelId);
        }
        else
        {
            body = BuildContext(new { continuation }, ClientIdentity.Web);
        }

        var json = await PostAsync("browse", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParseChannelPage(json, channelId);
    }

    private object BuildChannelBrowseBody(string channelId)
    {
        var id = ClientIdentity.Web;
        return new Dictionary<string, object?>
        {
            ["context"] = new
            {
                client = new
                {
                    clientName = "WEB",
                    clientVersion = id.ClientVersion,
                    hl = "en",
                    gl = "US"
                }
            },
            ["browseId"] = channelId
        };
    }

    private ChannelPage ParseChannelPage(JsonNode json, string channelId)
    {
        var channel = ParseChannel(json, channelId);
        var items = new List<FeedItem>();
        TryExtractVideos(json, items);
        var videos = items.OfType<VideoFeedItem>()
            .Select(v => v.Video)
            .GroupBy(v => v.Id)
            .Select(g => g.First())
            .Where(v => !IsShortVideo(v))
            .ToList();

        return new ChannelPage
        {
            Channel = channel,
            Videos = videos,
            ContinuationToken = FindContinuation(json)
        };
    }

    // ── Playlists ────────────────────────────────────────────────────────

    public async Task<PlaylistPage> GetPlaylistAsync(string playlistId, string? continuation = null, CancellationToken ct = default)
    {
        // browseId for playlists is often "VL" + playlistId
        var browseId = playlistId.StartsWith("VL", StringComparison.Ordinal)
            ? playlistId
            : "VL" + playlistId;

        object body = continuation is null
            ? new Dictionary<string, object?>
            {
                ["context"] = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = ClientIdentity.Web.ClientVersion,
                        hl = "en",
                        gl = "US"
                    }
                },
                ["browseId"] = browseId
            }
            : BuildContext(new { continuation }, ClientIdentity.Web);

        var json = await PostAsync("browse", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParsePlaylistPage(json, playlistId);
    }

    private PlaylistPage ParsePlaylistPage(JsonNode json, string playlistId)
    {
        var header = json["header"]?["playlistHeaderRenderer"];
        var title = header?["title"]?["simpleText"]?.GetValue<string>()
                 ?? header?["title"]?["runs"]?[0]?["text"]?.GetValue<string>()
                 ?? "Playlist";
        var countText = header?["numVideosText"]?["runs"]?[0]?["text"]?.GetValue<string>();
        int? count = null;
        if (countText is not null)
        {
            var digits = new string(countText.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var n)) count = n;
        }

        var items = new List<FeedItem>();
        TryExtractVideos(json, items);
        // also playlistVideoRenderer
        ExtractPlaylistVideos(json, items);

        var videos = items.OfType<VideoFeedItem>()
            .Select(v => v.Video)
            .GroupBy(v => v.Id)
            .Select(g => g.First())
            .ToList();

        return new PlaylistPage
        {
            Playlist = new Playlist
            {
                Id = playlistId.StartsWith("VL") ? playlistId[2..] : playlistId,
                Title = title,
                VideoCount = count,
                ThumbnailUrl = videos.FirstOrDefault()?.ThumbnailUrl
            },
            Videos = videos,
            ContinuationToken = FindContinuation(json)
        };
    }

    private static void ExtractPlaylistVideos(JsonNode node, List<FeedItem> items)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("playlistVideoRenderer", out var pvr) && pvr is not null)
            {
                var v = ParsePlaylistVideoRenderer(pvr);
                if (v is not null) items.Add(new VideoFeedItem { Video = v });
            }
            foreach (var kv in obj)
                if (kv.Value is not null) ExtractPlaylistVideos(kv.Value, items);
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr)
                if (child is not null) ExtractPlaylistVideos(child, items);
        }
    }

    private static Video? ParsePlaylistVideoRenderer(JsonNode pvr)
    {
        var id = pvr["videoId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;
        var title = pvr["title"]?["runs"]?[0]?["text"]?.GetValue<string>()
                 ?? pvr["title"]?["simpleText"]?.GetValue<string>()
                 ?? "Video";
        var length = pvr["lengthText"]?["simpleText"]?.GetValue<string>();
        TimeSpan? duration = null;
        if (!string.IsNullOrEmpty(length))
        {
            var parts = length.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s))
                duration = TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
            else if (parts.Length == 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out s))
                duration = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m) + TimeSpan.FromSeconds(s);
        }
        return new Video
        {
            Id = id,
            Title = title,
            Duration = duration,
            ChannelTitle = pvr["shortBylineText"]?["runs"]?[0]?["text"]?.GetValue<string>(),
            ThumbnailUrl = pvr["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>()
        };
    }

    public async Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default)
    {
        // FElibrary / FEplaylist_aggregation — requires auth; use TV client + bearer
        var body = new Dictionary<string, object?>
        {
            ["context"] = new
            {
                client = new
                {
                    clientName = "TVHTML5",
                    clientVersion = ClientIdentity.Tv.ClientVersion,
                    hl = "en",
                    gl = "US"
                }
            },
            ["browseId"] = "FElibrary"
        };
        try
        {
            var json = await PostAsync("browse", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
            return ParseLibraryPlaylists(json);
        }
        catch
        {
            return Array.Empty<Playlist>();
        }
    }

    private static IReadOnlyList<Playlist> ParseLibraryPlaylists(JsonNode json)
    {
        var list = new List<Playlist>();
        WalkPlaylists(json, list);
        return list.GroupBy(p => p.Id).Select(g => g.First()).ToList();
    }

    private static void WalkPlaylists(JsonNode node, List<Playlist> list)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("gridPlaylistRenderer", out var gpr) && gpr is not null)
            {
                var id = gpr["playlistId"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    list.Add(new Playlist
                    {
                        Id = id,
                        Title = gpr["title"]?["runs"]?[0]?["text"]?.GetValue<string>()
                             ?? gpr["title"]?["simpleText"]?.GetValue<string>()
                             ?? "Playlist",
                        VideoCount = int.TryParse(
                            new string((gpr["videoCountShortText"]?["simpleText"]?.GetValue<string>() ?? "")
                                .Where(char.IsDigit).ToArray()), out var n) ? n : null,
                        ThumbnailUrl = gpr["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>()
                    });
                }
            }
            foreach (var kv in obj)
                if (kv.Value is not null) WalkPlaylists(kv.Value, list);
        }
        else if (node is JsonArray arr)
        {
            foreach (var c in arr)
                if (c is not null) WalkPlaylists(c, list);
        }
    }

    // ── Engagement: like / dislike / subscribe ───────────────────────────

    public async Task LikeAsync(string videoId, CancellationToken ct = default)
    {
        var body = BuildEngagementBody(new Dictionary<string, object?>
        {
            ["target"] = new { videoId }
        });
        await PostAsync("like/like", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
    }

    public async Task DislikeAsync(string videoId, CancellationToken ct = default)
    {
        var body = BuildEngagementBody(new Dictionary<string, object?>
        {
            ["target"] = new { videoId }
        });
        await PostAsync("like/dislike", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
    }

    public async Task RemoveLikeAsync(string videoId, CancellationToken ct = default)
    {
        var body = BuildEngagementBody(new Dictionary<string, object?>
        {
            ["target"] = new { videoId }
        });
        await PostAsync("like/removelike", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(string channelId, CancellationToken ct = default)
    {
        var body = BuildEngagementBody(new Dictionary<string, object?>
        {
            ["channelIds"] = new[] { channelId }
        });
        await PostAsync("subscription/subscribe", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
    }

    public async Task UnsubscribeAsync(string channelId, CancellationToken ct = default)
    {
        var body = BuildEngagementBody(new Dictionary<string, object?>
        {
            ["channelIds"] = new[] { channelId }
        });
        await PostAsync("subscription/unsubscribe", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
    }

    private object BuildEngagementBody(Dictionary<string, object?> extra)
    {
        var dict = new Dictionary<string, object?>
        {
            ["context"] = new
            {
                client = new
                {
                    clientName = "TVHTML5",
                    clientVersion = ClientIdentity.Tv.ClientVersion,
                    hl = "en",
                    gl = "US"
                }
            }
        };
        foreach (var kv in extra)
            dict[kv.Key] = kv.Value;
        return dict;
    }

    // ── Captions text (timedtext) ─────────────────────────────────────────

    public async Task<string?> FetchCaptionXmlAsync(string baseUrl, CancellationToken ct = default)
    {
        // Request VTT-ish or srv3 JSON
        var url = baseUrl;
        if (!url.Contains("fmt=", StringComparison.Ordinal))
            url += (url.Contains('?') ? "&" : "?") + "fmt=vtt";
        try
        {
            return await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }


    // ── Channel tabs (iOS ChannelTabParams) ───────────────────────────────

    public static class ChannelTabParams
    {
        public const string Videos = "EgZ2aWRlb3PyBgQKAjoA";
        public const string Shorts = "EgZzaG9ydHPyBgUKA5oBAA==";
        public const string Live = "EgZzdm9ybHPyBgQKAjoA"; // may vary
        public const string Playlists = "EglwbGF5bGlzdHPyBgQKAkIA";
    }

    public async Task<ChannelPage> GetChannelTabAsync(
        string channelId, string paramsToken, string? continuation = null, CancellationToken ct = default)
    {
        object body;
        if (continuation is null)
        {
            body = new Dictionary<string, object?>
            {
                ["context"] = new
                {
                    client = new
                    {
                        clientName = "WEB",
                        clientVersion = ClientIdentity.Web.ClientVersion,
                        hl = "en",
                        gl = "US"
                    }
                },
                ["browseId"] = channelId,
                ["params"] = paramsToken
            };
        }
        else
            body = BuildContext(new { continuation }, ClientIdentity.Web);

        var json = await PostAsync("browse", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParseChannelPage(json, channelId);
    }

    // ── Playlist edit (iOS executeEditPlaylist) ───────────────────────────

    public async Task<bool> EditPlaylistAsync(
        string playlistId,
        IReadOnlyList<PlaylistEditAction> actions,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["context"] = new
            {
                client = new
                {
                    clientName = "TVHTML5",
                    clientVersion = ClientIdentity.Tv.ClientVersion,
                    hl = "en",
                    gl = "US",
                    platform = "TV"
                }
            },
            ["playlistId"] = playlistId,
            ["actions"] = actions.Select(a => a.ToDictionary()).ToArray()
        };
        var json = await PostAsync("browse/edit_playlist", body, ClientIdentity.Tv, sendAuth: true, ct).ConfigureAwait(false);
        var status = json["status"]?.GetValue<string>();
        return status == "STATUS_SUCCEEDED";
    }

    public Task<bool> AddVideoToPlaylistAsync(string playlistId, string videoId, CancellationToken ct = default)
        => EditPlaylistAsync(playlistId, new[]
        {
            new PlaylistEditAction { Action = "ACTION_ADD_VIDEO", AddedVideoId = videoId }
        }, ct);

    public Task<bool> RemoveVideoFromPlaylistAsync(string playlistId, string videoId, CancellationToken ct = default)
        => EditPlaylistAsync(playlistId, new[]
        {
            new PlaylistEditAction { Action = "ACTION_REMOVE_VIDEO_BY_VIDEO_ID", RemovedVideoId = videoId }
        }, ct);

    /// <summary>Fetch caption tracks via IOS player (timedtext without pot).</summary>
    public async Task<IReadOnlyList<CaptionTrack>> FetchCaptionTracksIosAsync(string videoId, CancellationToken ct = default)
    {
        var body = BuildContext(new
        {
            videoId,
            contentCheckOk = true,
            racyCheckOk = true
        }, ClientIdentity.Ios);
        var json = await PostAsync("player", body, ClientIdentity.Ios, sendAuth: false, ct).ConfigureAwait(false);
        return ParseCaptionTracks(json);
    }
}
