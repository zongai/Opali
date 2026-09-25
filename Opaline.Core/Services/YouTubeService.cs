using Opaline.Core.Api;
using Opaline.Core.Models;

namespace Opaline.Core.Services;

public sealed class YouTubeService : IYouTubeService
{
    private readonly InnertubeClient _client;

    public YouTubeService(InnertubeClient client) => _client = client;

    public Task<HomeFeed> GetHomeAsync(string? continuation = null, CancellationToken ct = default)
        => _client.GetHomeFeedAsync(continuation, ct);

    public Task<HomeFeed> GetSubscriptionsAsync(string? continuation = null, CancellationToken ct = default)
        => _client.GetSubscriptionsFeedAsync(continuation, ct);

    public Task<HomeFeed> GetShortsAsync(string? continuation = null, CancellationToken ct = default)
        => _client.GetShortsFeedAsync(continuation, ct);

    public Task<WatchPage> GetWatchAsync(string videoId, CancellationToken ct = default)
        => _client.GetWatchPageAsync(videoId, ct);

    public Task<SearchPage> SearchAsync(string query, string? continuation = null, CancellationToken ct = default)
        => _client.SearchAsync(query, continuation, ct);

    public Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken ct = default)
        => _client.GetSearchSuggestionsAsync(query, ct);

    public Task<Channel> GetChannelAsync(string channelId, CancellationToken ct = default)
        => _client.GetChannelAsync(channelId, ct);

    public Task<CommentsPage> GetCommentsAsync(string videoId, string? continuation = null, CancellationToken ct = default)
        => _client.GetCommentsAsync(videoId, continuation, ct);
}
