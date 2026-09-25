using System.Text;
using System.Text.Json.Nodes;
using Opaline.Core.Auth;
using Opaline.Core.Models;

namespace Opaline.Core.Api;

public sealed partial class InnertubeClient
{
    private OAuthClient? _oauth;

    public void AttachAuth(OAuthClient oauth) => _oauth = oauth;

    /// <summary>Shorts-oriented home extraction (duration ≤ 60s or IsShort).</summary>
    public async Task<HomeFeed> GetShortsFeedAsync(string? continuation = null, CancellationToken ct = default)
    {
        var body = continuation is null
            ? BuildContext(new { browseId = "FEwhat_to_watch" }, ClientIdentity.Web)
            : BuildContext(new { continuation }, ClientIdentity.Web);

        var json = await PostAsync("browse", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        var feed = ParseHomeFeed(json);

        var shorts = feed.Items
            .OfType<VideoFeedItem>()
            .Select(v =>
            {
                // Mark as short when duration suggests it
                if (v.Video.Duration is { } d && d.TotalSeconds <= 60)
                {
                    return new VideoFeedItem
                    {
                        Id = v.Id,
                        Video = new Video
                        {
                            Id = v.Video.Id,
                            Title = v.Video.Title,
                            Description = v.Video.Description,
                            ChannelId = v.Video.ChannelId,
                            ChannelTitle = v.Video.ChannelTitle,
                            ChannelAvatarUrl = v.Video.ChannelAvatarUrl,
                            ThumbnailUrl = v.Video.ThumbnailUrl,
                            Duration = v.Video.Duration,
                            ViewCount = v.Video.ViewCount,
                            PublishedAt = v.Video.PublishedAt,
                            IsLive = v.Video.IsLive,
                            IsShort = true
                        }
                    };
                }
                return v;
            })
            .Where(v => v.Video.IsShort || (v.Video.Duration is { } d2 && d2.TotalSeconds <= 60))
            .Cast<FeedItem>()
            .ToList();

        if (shorts.Count == 0)
            return feed;

        return new HomeFeed { Items = shorts, ContinuationToken = feed.ContinuationToken };
    }


    /// <summary>Load comments via /next (WEB). First call resolves entry continuation.</summary>
    public async Task<CommentsPage> GetCommentsAsync(string videoId, string? continuation = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(continuation))
        {
            var nextBody = BuildContext(new { videoId }, ClientIdentity.Web);
            var nextJson = await PostAsync("next", nextBody, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
            continuation = FindCommentsContinuation(nextJson);
            System.Diagnostics.Debug.WriteLine($"[Comments] entry continuation={(continuation is null ? "null" : continuation[..Math.Min(24, continuation.Length)] + "…")}");
            if (continuation is null)
                return new CommentsPage();
        }

        var body = BuildContext(new { continuation }, ClientIdentity.Web);
        var json = await PostAsync("next", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
        return ParseComments(json);
    }

    private static string? FindCommentsContinuation(JsonNode json)
    {
        // Prefer explicit comments section continuation tokens
        string? found = null;
        void Walk(JsonNode? n)
        {
            if (found is not null || n is null) return;
            if (n is JsonObject obj)
            {
                // commentsEntryPointHeaderRenderer / section list continuations
                if (obj.TryGetPropertyValue("continuationEndpoint", out var ep) && ep is not null)
                {
                    var token = ep["continuationCommand"]?["token"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(token) && token.Length > 20)
                    {
                        // Heuristic: comment tokens often contain "comments" context; accept first solid token under engagement
                        found = token;
                        return;
                    }
                }
                if (obj.TryGetPropertyValue("continuationItemRenderer", out var cir) && cir is not null)
                {
                    var token = cir["continuationEndpoint"]?["continuationCommand"]?["token"]?.GetValue<string>()
                             ?? cir["button"]?["buttonRenderer"]?["command"]?["continuationCommand"]?["token"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(token))
                    {
                        found = token;
                        return;
                    }
                }
                foreach (var kv in obj)
                    Walk(kv.Value);
            }
            else if (n is JsonArray arr)
            {
                foreach (var c in arr) Walk(c);
            }
        }
        Walk(json);
        return found;
    }

    private static CommentsPage ParseComments(JsonNode json)
    {
        var list = new List<CommentThread>();
        void Walk(JsonNode? n)
        {
            if (n is null) return;
            if (n is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("commentThreadRenderer", out var thr) && thr is not null)
                {
                    var c = thr["comment"]?["commentRenderer"];
                    if (c is not null)
                    {
                        var id = c["commentId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                        var runs = c["contentText"]?["runs"] as JsonArray;
                        var text = runs is null
                            ? (c["contentText"]?["simpleText"]?.GetValue<string>() ?? "")
                            : string.Concat(runs.Select(r => r?["text"]?.GetValue<string>() ?? ""));
                        var author = c["authorText"]?["simpleText"]?.GetValue<string>()
                                  ?? c["authorText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>()
                                  ?? "";
                        var avatar = c["authorThumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>();
                        var published = c["publishedTimeText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>()
                                     ?? c["publishedTimeText"]?["simpleText"]?.GetValue<string>();
                        long likes = 0;
                        if (c["voteCount"]?["simpleText"] is JsonNode vc)
                        {
                            var s = vc.GetValue<string>()?.Replace(",", "") ?? "0";
                            if (s.EndsWith("K", StringComparison.OrdinalIgnoreCase) &&
                                double.TryParse(s[..^1], out var k)) likes = (long)(k * 1000);
                            else long.TryParse(s, out likes);
                        }
                        list.Add(new CommentThread
                        {
                            Id = id,
                            AuthorName = author,
                            AuthorAvatarUrl = avatar,
                            Text = text,
                            PublishedTime = published,
                            LikeCount = likes,
                            ReplyCount = c["replyCount"]?.GetValue<int>() ?? 0
                        });
                    }
                }
                foreach (var kv in obj) Walk(kv.Value);
            }
            else if (n is JsonArray arr)
            {
                foreach (var c in arr) Walk(c);
            }
        }
        Walk(json);
        return new CommentsPage
        {
            Comments = list,
            ContinuationToken = FindCommentsContinuation(json)
        };
    }

    private async Task<JsonNode> PostAsync(
        string endpoint,
        object body,
        ClientIdentity? identity,
        bool sendAuth,
        CancellationToken ct)
    {
        var id = identity ?? _identity;
        var url = $"https://www.youtube.com/youtubei/v1/{endpoint}?key={id.ApiKey}&prettyPrint=false";
        using var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.TryAddWithoutValidation("User-Agent", id.UserAgent);
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", id.ClientNameId);
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", id.ClientVersion);
        request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        // TV bearer tokens pair with /tv referer; WEB stays on www
        request.Headers.TryAddWithoutValidation(
            "Referer",
            id.ClientName == "TVHTML5" ? "https://www.youtube.com/tv" : "https://www.youtube.com/");

        // Device-code OAuth is a TV token. Attaching it to WEB requests causes
        // INVALID_ARGUMENT. Only send Bearer for TV (and explicit opt-in).
        if (sendAuth && _oauth is not null)
        {
            var token = await _oauth.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine(
            $"[InnerTube] {endpoint}/{id.ClientName} {(int)response.StatusCode} {raw.Length}b {sw.ElapsedMilliseconds}ms auth={sendAuth}");
        if (!response.IsSuccessStatusCode)
        {
            var snippet = raw.Length > 500 ? raw[..500] : raw;
            throw new HttpRequestException(
                $"InnerTube {endpoint}/{id.ClientName} {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
        }
        var node = JsonNode.Parse(raw);
        return node ?? throw new InvalidOperationException("Empty InnerTube response");
    }
}
