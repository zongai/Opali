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


public sealed class PlaylistEditAction
{
    public string Action { get; init; } = "ACTION_ADD_VIDEO";
    public string? AddedVideoId { get; init; }
    public string? RemovedVideoId { get; init; }

    public Dictionary<string, object?> ToDictionary()
    {
        var d = new Dictionary<string, object?> { ["action"] = Action };
        if (AddedVideoId is not null) d["addedVideoId"] = AddedVideoId;
        if (RemovedVideoId is not null) d["removedVideoId"] = RemovedVideoId;
        return d;
    }
}
