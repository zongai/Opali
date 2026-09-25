namespace Opaline.Core.Models;

public sealed class Channel
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string? Description { get; init; }
    public string? AvatarUrl { get; init; }
    public string? BannerUrl { get; init; }
    public long? SubscriberCount { get; init; }
    public bool IsVerified { get; init; }
    public bool IsSubscribed { get; set; }

    public string FormattedSubscribers => SubscriberCount switch
    {
        null => string.Empty,
        >= 1_000_000 => $"{SubscriberCount.Value / 1_000_000.0:0.#}M subscribers",
        >= 1_000 => $"{SubscriberCount.Value / 1_000.0:0.#}K subscribers",
        _ => $"{SubscriberCount} subscribers"
    };
}
