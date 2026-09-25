namespace Opaline.Core.Models;

public sealed class Playlist
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? ChannelTitle { get; init; }
    public int? VideoCount { get; init; }
    public IReadOnlyList<Video>? Videos { get; init; }
}
