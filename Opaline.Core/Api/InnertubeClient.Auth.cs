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
            ? BuildContext(new { browseId = "FEwhat_to_watch" })
            : BuildContext(new { continuation });

        var json = await PostAsync("browse", body, ct).ConfigureAwait(false);
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

    private async Task<JsonNode> PostAsync(string endpoint, object body, CancellationToken ct)
    {
        var url = $"https://www.youtube.com/youtubei/v1/{endpoint}?key={_identity.ApiKey}&prettyPrint=false";
        using var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (_oauth is not null)
        {
            var token = await _oauth.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return node ?? throw new InvalidOperationException("Empty InnerTube response");
    }
}
