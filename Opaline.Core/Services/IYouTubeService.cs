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
    Task<CommentsPage> GetCommentsAsync(string videoId, string? continuation = null, CancellationToken ct = default);
    Task<ChannelPage> GetChannelAsync(string channelId, string? continuation = null, CancellationToken ct = default);
    Task<PlaylistPage> GetPlaylistAsync(string playlistId, string? continuation = null, CancellationToken ct = default);
    Task<IReadOnlyList<Playlist>> GetLibraryPlaylistsAsync(CancellationToken ct = default);
    Task LikeAsync(string videoId, CancellationToken ct = default);
    Task DislikeAsync(string videoId, CancellationToken ct = default);
    Task RemoveLikeAsync(string videoId, CancellationToken ct = default);
    Task SubscribeAsync(string channelId, CancellationToken ct = default);
    Task UnsubscribeAsync(string channelId, CancellationToken ct = default);
    Task<string?> FetchCaptionAsync(string baseUrl, CancellationToken ct = default);

    Task<ChannelPage> GetChannelTabAsync(string channelId, string paramsToken, string? continuation = null, CancellationToken ct = default);
    Task<bool> AddVideoToPlaylistAsync(string playlistId, string videoId, CancellationToken ct = default);
    Task<bool> RemoveVideoFromPlaylistAsync(string playlistId, string videoId, CancellationToken ct = default);
    Task<IReadOnlyList<CaptionTrack>> FetchCaptionTracksIosAsync(string videoId, CancellationToken ct = default);
}
