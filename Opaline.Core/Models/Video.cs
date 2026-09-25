namespace Opaline.Core.Models;

/// <summary>
/// Core video model — mirrors Opaline iOS Video.
/// </summary>
public sealed class Video
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string? Description { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelTitle { get; init; }
    public string? ChannelAvatarUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public TimeSpan? Duration { get; init; }
    public long? ViewCount { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool IsLive { get; init; }
    public bool IsShort { get; init; }
    public string? LiveConcurrentViewers { get; init; }

    public string FormattedDuration => Duration is { } d
        ? d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}"
        : string.Empty;

    public string FormattedViewCount => ViewCount switch
    {
        null => string.Empty,
        >= 1_000_000_000 => $"{ViewCount.Value / 1_000_000_000.0:0.#}B views",
        >= 1_000_000 => $"{ViewCount.Value / 1_000_000.0:0.#}M views",
        >= 1_000 => $"{ViewCount.Value / 1_000.0:0.#}K views",
        _ => $"{ViewCount} views"
    };
}
