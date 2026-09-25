namespace Opaline.Core.Models;

public sealed class HomeFeed
{
    public required IReadOnlyList<FeedItem> Items { get; init; }
    public string? ContinuationToken { get; init; }
}

public abstract class FeedItem
{
    public required string Id { get; init; }
}

public sealed class VideoFeedItem : FeedItem
{
    public required Video Video { get; init; }
}

public sealed class ShelfFeedItem : FeedItem
{
    public required string Title { get; init; }
    public required IReadOnlyList<Video> Videos { get; init; }
}

public sealed class CategoryChip
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool IsSelected { get; set; }
}
