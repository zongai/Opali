namespace Opaline.Core.Models;

public sealed class CommentThread
{
    public string Id { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string? AuthorAvatarUrl { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? PublishedTime { get; init; }
    public long LikeCount { get; init; }
    public int ReplyCount { get; init; }
}

public sealed class CommentsPage
{
    public IReadOnlyList<CommentThread> Comments { get; init; } = Array.Empty<CommentThread>();
    public string? ContinuationToken { get; init; }
    public long? TotalCount { get; init; }
}
