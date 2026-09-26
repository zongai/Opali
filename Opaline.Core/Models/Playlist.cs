namespace Opaline.Core.Models;

public sealed class Playlist
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? Author { get; init; }
    public string? AuthorId { get; init; }
    public int? VideoCount { get; init; }
}
