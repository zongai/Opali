namespace Opaline.Core.Models;

public sealed class SearchPage
{
    public required IReadOnlyList<SearchItem> Items { get; init; }
    public string? ContinuationToken { get; init; }
}

public abstract class SearchItem
{
    public required string Id { get; init; }
}

public sealed class VideoSearchItem : SearchItem
{
    public required Video Video { get; init; }
}

public sealed class ChannelSearchItem : SearchItem
{
    public required Channel Channel { get; init; }
}

public sealed class PlaylistSearchItem : SearchItem
{
    public required Playlist Playlist { get; init; }
}
