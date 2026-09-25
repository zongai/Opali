namespace Opaline.Core.Models;

public sealed class HomeFeed
{
    public IReadOnlyList<FeedItem> Items { get; init; }
    public string? ContinuationToken { get; init; }
}

public abstract class FeedItem
{
    public string Id { get; init; }
}

public sealed class VideoFeedItem : FeedItem
{
    public Video Video { get; init; }
}

public sealed class ShelfFeedItem : FeedItem
{
    public string Title { get; init; }
    public IReadOnlyList<Video> Videos { get; init; }
}

public sealed class CategoryChip
{
    public string Id { get; init; }
    public string Title { get; init; }
    public bool IsSelected { get; set; }
}
