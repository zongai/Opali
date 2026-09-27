namespace Opaline.Core.Models;

public sealed class FeedbackAction
{
    public string Token { get; init; } = "";
    public string Label { get; init; } = "";
    public string Kind { get; init; } = "generic"; // not_interested | dont_recommend_channel | generic
}
