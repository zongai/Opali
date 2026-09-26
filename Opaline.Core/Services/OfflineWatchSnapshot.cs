using Opaline.Core.Models;

namespace Opaline.Core.Services;

/// <summary>
/// Disk-side WatchPage metadata saved next to a download (iOS DownloadStore page.json).
/// Stream URLs are not relied on offline — local media path is used for playback.
/// </summary>
public sealed class OfflineWatchSnapshot
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? ChannelId { get; set; }
    public string? ChannelTitle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? LikeCount { get; set; }
    public string? DislikeCount { get; set; }
    public long? ViewCount { get; set; }
    public double? DurationSeconds { get; set; }
    public List<OfflineRelatedItem> RelatedVideos { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    public static OfflineWatchSnapshot FromWatchPage(WatchPage page)
    {
        var v = page.Video;
        return new OfflineWatchSnapshot
        {
            VideoId = v.Id,
            Title = v.Title ?? v.Id,
            Description = v.Description,
            ChannelId = v.ChannelId,
            ChannelTitle = v.ChannelTitle,
            ThumbnailUrl = v.ThumbnailUrl,
            LikeCount = page.LikeCount,
            DislikeCount = page.DislikeCount,
            ViewCount = v.ViewCount,
            DurationSeconds = v.Duration?.TotalSeconds,
            RelatedVideos = (page.RelatedVideos ?? Array.Empty<Video>())
                .Select(r => new OfflineRelatedItem
                {
                    VideoId = r.Id,
                    Title = r.Title ?? r.Id,
                    ChannelTitle = r.ChannelTitle,
                    ThumbnailUrl = r.ThumbnailUrl,
                    ViewCount = r.ViewCount
                }).ToList(),
            SavedAt = DateTimeOffset.UtcNow
        };
    }

    public WatchPage ToWatchPage(IReadOnlyList<Video>? offlineRelated = null)
    {
        var related = offlineRelated
            ?? RelatedVideos.Select(r => r.ToVideo()).ToList();
        return new WatchPage
        {
            Video = new Video
            {
                Id = VideoId,
                Title = Title,
                Description = Description,
                ChannelId = ChannelId,
                ChannelTitle = ChannelTitle,
                ThumbnailUrl = ThumbnailUrl,
                ViewCount = ViewCount,
                Duration = DurationSeconds is double d ? TimeSpan.FromSeconds(d) : null
            },
            Streams = Array.Empty<StreamInfo>(),
            RelatedVideos = related,
            LikeCount = LikeCount,
            DislikeCount = DislikeCount
        };
    }
}

public sealed class OfflineRelatedItem
{
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ChannelTitle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long? ViewCount { get; set; }

    public Video ToVideo() => new()
    {
        Id = VideoId,
        Title = Title,
        ChannelTitle = ChannelTitle,
        ThumbnailUrl = ThumbnailUrl,
        ViewCount = ViewCount
    };
}
