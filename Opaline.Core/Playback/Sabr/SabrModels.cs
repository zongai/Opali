namespace Opaline.Core.Playback.Sabr;

public sealed class SabrFormatInfo
{
    public int Itag { get; init; }
    public string? LastModified { get; init; }
    public string? Xtags { get; init; }
    public string? AudioTrackId { get; init; }
    public string? MimeType { get; init; }
    public long? Bitrate { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}

public sealed class SabrBufferedRange
{
    public required SabrFormatInfo Format { get; init; }
    public int StartMs { get; init; }
    public int DurationMs { get; init; }
    public int StartSequence { get; init; }
    public int EndSequence { get; init; }
    public int Timescale { get; init; } = 1000;
}

public enum SabrTrackWants
{
    Both = 3,
    Video = 2,
    Audio = 1,
}

public sealed class SabrPlayerState
{
    public required SabrFormatInfo Video { get; init; }
    public string? AudioTrackId { get; init; }
    public SabrTrackWants Wants { get; init; } = SabrTrackWants.Both;
    public int PlayerMs { get; init; }
}
