using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Opaline.Core.Auth;
using Opaline.Core.Models;

namespace Opaline.Core.Api;

public sealed partial class InnertubeClient
{
    private OAuthClient? _oauth;

    public void AttachAuth(OAuthClient oauth) => _oauth = oauth;

    /// <summary>
    /// Shorts feed via /reel/reel_watch_sequence (iOS ShortsSeed + parseShortsSequence).
    /// <paramref name="continuation"/> is either a server continuation token or a seed videoId (11 chars).
    /// </summary>
    public async Task<HomeFeed> GetShortsFeedAsync(string? continuation = null, CancellationToken ct = default)
    {
        // iOS ShortsSeed.cold = base64([0x10, 0x01]); from video = field1 length-delimited videoId
        string sequenceParams;
        if (string.IsNullOrEmpty(continuation))
            sequenceParams = Convert.ToBase64String(new byte[] { 0x10, 0x01 });
        else if (continuation.Length == 11 && continuation.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
        {
            var idBytes = System.Text.Encoding.UTF8.GetBytes(continuation);
            var buf = new byte[2 + idBytes.Length];
            buf[0] = 0x0A;
            buf[1] = (byte)idBytes.Length;
            Buffer.BlockCopy(idBytes, 0, buf, 2, idBytes.Length);
            sequenceParams = Convert.ToBase64String(buf);
        }
        else
            sequenceParams = continuation; // raw continuation token from prior page

        var signedIn = _oauth is not null && _oauth.IsSignedIn;
        var id = signedIn ? ClientIdentity.Tv : ClientIdentity.Web;
        var body = BuildContext(new { sequenceParams }, id);
        // BuildContext only merges endpoint props; sequenceParams is on the anonymous object

        var json = await PostAsync("reel/reel_watch_sequence", body, id, sendAuth: signedIn, ct).ConfigureAwait(false);
        CaptureVisitorData(json);
        return ParseShortsSequence(json);
    }

    private static HomeFeed ParseShortsSequence(JsonNode json)
    {
        var items = new List<FeedItem>();
        if (json["entries"] is JsonArray entries)
        {
            foreach (var entry in entries)
            {
                var ep = entry?["command"]?["reelWatchEndpoint"];
                var videoId = ep?["videoId"]?.GetValue<string>();
                if (string.IsNullOrEmpty(videoId)) continue;
                var thumb = ep?["thumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>();
                items.Add(new VideoFeedItem
                {
                    Id = videoId,
                    Video = new Video
                    {
                        Id = videoId,
                        Title = "Short",
                        ThumbnailUrl = thumb,
                        IsShort = true
                    }
                });
            }
        }

        // Prefer server continuation; else seed next page from last videoId (iOS sequence)
        var cont = json["continuationEndpoint"]?["continuationCommand"]?["token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(cont) && items.Count > 0)
            cont = items[^1].Id;

        return new HomeFeed { Items = items, ContinuationToken = cont };
    }


    /// <summary>
    /// Comments via /next with protobuf continuation (iOS buildCommentsContinuation + executeComments).
    /// </summary>
    public async Task<CommentsPage> GetCommentsAsync(string videoId, string? continuation = null, CancellationToken ct = default)
    {
        continuation ??= BuildCommentsContinuation(videoId, sortBy: 0);
        // Prefer WEB (matches desktop comment panel); fall back to ANDROID if empty.
        foreach (var client in new[] { ClientIdentity.Web, ClientIdentity.Android })
        {
            var body = BuildContext(new { continuation }, client);
            var json = await PostAsync("next", body, client, sendAuth: false, ct).ConfigureAwait(false);
            CaptureVisitorData(json);
            var page = ParseComments(json);
            if (page.Comments.Count > 0 || !string.IsNullOrEmpty(page.Continuation))
                return page;
        }
        // Last resort: open watch next without synthetic continuation (engagement panels)
        {
            var body = BuildContext(new { videoId }, ClientIdentity.Web);
            var json = await PostAsync("next", body, ClientIdentity.Web, sendAuth: false, ct).ConfigureAwait(false);
            CaptureVisitorData(json);
            return ParseComments(json);
        }
    }

    /// <summary>Port of iOS InnertubeClient.buildCommentsContinuation (sortBy 0 = top).</summary>
    private static string BuildCommentsContinuation(string videoId, int sortBy)
    {
        // proto helpers (length-delimited string / varint)
        static byte[] Varint(int value)
        {
            var bytes = new List<byte>();
            uint v = (uint)value;
            while (v >= 0x80)
            {
                bytes.Add((byte)(v | 0x80));
                v >>= 7;
            }
            bytes.Add((byte)v);
            return bytes.ToArray();
        }
        static byte[] Key(int field, int wireType) => Varint((field << 3) | wireType);
        static byte[] ProtoString(int field, string value)
        {
            var payload = Encoding.UTF8.GetBytes(value);
            var head = Key(field, 2);
            var len = Varint(payload.Length);
            var buf = new byte[head.Length + len.Length + payload.Length];
            Buffer.BlockCopy(head, 0, buf, 0, head.Length);
            Buffer.BlockCopy(len, 0, buf, head.Length, len.Length);
            Buffer.BlockCopy(payload, 0, buf, head.Length + len.Length, payload.Length);
            return buf;
        }
        static byte[] ProtoInt32(int field, int value)
        {
            var head = Key(field, 0);
            var body = Varint(value);
            var buf = new byte[head.Length + body.Length];
            Buffer.BlockCopy(head, 0, buf, 0, head.Length);
            Buffer.BlockCopy(body, 0, buf, head.Length, body.Length);
            return buf;
        }
        static byte[] ProtoMessage(int field, byte[] value)
        {
            var head = Key(field, 2);
            var len = Varint(value.Length);
            var buf = new byte[head.Length + len.Length + value.Length];
            Buffer.BlockCopy(head, 0, buf, 0, head.Length);
            Buffer.BlockCopy(len, 0, buf, head.Length, len.Length);
            Buffer.BlockCopy(value, 0, buf, head.Length + len.Length, value.Length);
            return buf;
        }
        static byte[] Concat(params byte[][] parts)
        {
            var n = parts.Sum(p => p.Length);
            var buf = new byte[n];
            var o = 0;
            foreach (var p in parts)
            {
                Buffer.BlockCopy(p, 0, buf, o, p.Length);
                o += p.Length;
            }
            return buf;
        }

        var ctx = ProtoString(2, videoId);
        var opts = Concat(
            ProtoString(4, videoId),
            ProtoInt32(6, sortBy),
            ProtoInt32(15, 2));
        var paramsMsg = Concat(
            ProtoMessage(4, opts),
            ProtoString(8, "comments-section"));
        var root = Concat(
            ProtoMessage(2, ctx),
            ProtoInt32(3, 6),
            ProtoMessage(6, paramsMsg));

        // base64url (YouTube expects unescaped token in JSON body)
        return Convert.ToBase64String(root).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string? FindCommentsContinuation(JsonNode json)
    {
        string? found = null;
        void Walk(JsonNode? n)
        {
            if (found is not null || n is null) return;
            if (n is JsonObject obj)
            {
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
        if (list.Count == 0)
        {
            // Flat commentRenderer nodes (some clients)
            void WalkFlat(JsonNode? n)
            {
                if (n is null) return;
                if (n is JsonObject obj)
                {
                    if (obj.TryGetPropertyValue("commentRenderer", out var c) && c is not null
                        && !obj.ContainsKey("commentThreadRenderer"))
                    {
                        var id = c["commentId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                        if (list.Any(x => x.Id == id)) { }
                        else
                        {
                            var runs = c["contentText"]?["runs"] as JsonArray;
                            var text = runs is null
                                ? (c["contentText"]?["simpleText"]?.GetValue<string>() ?? "")
                                : string.Concat(runs.Select(r => r?["text"]?.GetValue<string>() ?? ""));
                            var author = c["authorText"]?["simpleText"]?.GetValue<string>()
                                      ?? c["authorText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>()
                                      ?? "";
                            list.Add(new CommentThread
                            {
                                Id = id,
                                AuthorName = author,
                                Text = text,
                                PublishedTime = c["publishedTimeText"]?["runs"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>()
                                             ?? c["publishedTimeText"]?["simpleText"]?.GetValue<string>(),
                                LikeCount = 0,
                                AuthorAvatarUrl = c["authorThumbnail"]?["thumbnails"]?.AsArray()?.LastOrDefault()?["url"]?.GetValue<string>()
                            });
                        }
                    }
                    foreach (var kv in obj) WalkFlat(kv.Value);
                }
                else if (n is JsonArray arr)
                {
                    foreach (var c in arr) WalkFlat(c);
                }
            }
            WalkFlat(json);
        }
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
        var node = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Empty InnerTube response");
        CaptureVisitorData(node);
        return node;
    }
}
