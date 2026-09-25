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

        var json = await PostAsync("browse", body, ClientIdentity.Web, ct).ConfigureAwait(false);
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

    private async Task<JsonNode> PostAsync(
        string endpoint,
        object body,
        ClientIdentity? identity,
        CancellationToken ct)
    {
        var id = identity ?? _identity;
        var url = $"https://www.youtube.com/youtubei/v1/{endpoint}?key={id.ApiKey}&prettyPrint=false";
        using var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        // Per-request client headers (override any DefaultRequestHeaders)
        request.Headers.TryAddWithoutValidation("User-Agent", id.UserAgent);
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", id.ClientNameId);
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", id.ClientVersion);
        request.Headers.TryAddWithoutValidation("Origin", "https://www.youtube.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.youtube.com/");

        if (_oauth is not null)
        {
            var token = await _oauth.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
