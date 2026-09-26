namespace Opaline.Core.Models;

/// <summary>iOS AudioTrackInfo — one distinct audioTrack.id from /player.</summary>
public sealed class AudioTrackInfo
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsDefault { get; init; }

    /// <summary>Original upload language track ends with ".4".</summary>
    public bool IsOriginal => Id.EndsWith(".4", StringComparison.Ordinal);

    /// <summary>AI auto-dub tracks use ".10" suffix.</summary>
    public bool IsAutoDubbed => Id.Contains(".10", StringComparison.Ordinal);

    public string LanguageCode
    {
        get
        {
            var tag = Id.Split('.')[0];
            var baseCode = tag.Split('-')[0];
            return baseCode.ToLowerInvariant();
        }
    }
}
