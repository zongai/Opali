using Opaline.Core.Models;

namespace Opaline.Core.Services;

public interface IYouTubeService
{
    Task<HomeFeed> GetHomeAsync(string? continuation = null, CancellationToken ct = default);
    Task<HomeFeed> GetSubscriptionsAsync(string? continuation = null, CancellationToken ct = default);
    Task<HomeFeed> GetShortsAsync(string? continuation = null, CancellationToken ct = default);
    Task<WatchPage> GetWatchAsync(string videoId, CancellationToken ct = default);
    Task<SearchPage> SearchAsync(string query, string? continuation = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken ct = default);
    Task<Channel> GetChannelAsync(string channelId, CancellationToken ct = default);
}
