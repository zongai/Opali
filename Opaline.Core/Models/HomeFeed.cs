namespace Opaline.Core.Models;

public sealed class HomeFeed
{
    public IReadOnlyList<FeedItem> Items { get; init; }
    public string? ContinuationToken { get; init; }
}

public abstract class FeedItem
{
    public string Id { get; init; } = string.Empty;
}

public sealed class VideoFeedItem : FeedItem
{
    public Video Video { get; init; }
}

public sealed class ShelfFeedItem : FeedItem
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<Video> Videos { get; init; }
}

public sealed class CategoryChip
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}
