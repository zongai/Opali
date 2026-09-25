namespace Opaline.Core.Models;

public sealed class Playlist
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? ChannelTitle { get; init; }
    public int? VideoCount { get; init; }
    public IReadOnlyList<Video>? Videos { get; init; }
}
