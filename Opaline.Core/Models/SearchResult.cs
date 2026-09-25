namespace Opaline.Core.Models;

public sealed class SearchPage
{
    public IReadOnlyList<SearchItem> Items { get; init; }
    public string? ContinuationToken { get; init; }
}

public abstract class SearchItem
{
    public string Id { get; init; }
}

public sealed class VideoSearchItem : SearchItem
{
    public Video Video { get; init; }
}

public sealed class ChannelSearchItem : SearchItem
{
    public Channel Channel { get; init; }
}

public sealed class PlaylistSearchItem : SearchItem
{
    public Playlist Playlist { get; init; }
}
