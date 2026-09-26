namespace Opaline.Core.Playback.Sabr;

public sealed class MediaHeader
{
    public int Itag { get; init; }
    public string? Xtags { get; init; }
    public long StartRange { get; init; }
    public int Sequence { get; init; }
    public bool IsInit { get; init; }
    public int StartMs { get; init; }
    public int DurationMs { get; init; }
    public int Timescale { get; init; } = 1000;

    public static MediaHeader? TryParse(byte[] payload)
    {
        var fields = SabrProtobuf.Parse(payload);
        var itag = fields.Number(3);
        if (itag is null) return null;
        var formatId = fields.Data(13);
        var formatFields = formatId is null ? null : SabrProtobuf.Parse(formatId);
        var time = fields.Data(15);
        var timeFields = time is null ? null : SabrProtobuf.Parse(time);
        var timescale = (int)(timeFields?.Number(3) ?? 1000);
        static int Ms(long? ticks, int ts) =>
            ticks is null || ts <= 0 ? 0 : (int)(ticks.Value * 1000 / ts);

        return new MediaHeader
        {
            Itag = (int)itag.Value,
            Xtags = fields.Str(5) ?? formatFields?.Str(3),
            StartRange = fields.Number(6) ?? 0,
            Sequence = (int)(fields.Number(9) ?? 0),
            IsInit = (fields.Number(8) ?? 0) != 0,
            Timescale = timescale,
            StartMs = (int)(fields.Number(11) ?? Ms(timeFields?.Number(1), timescale)),
            DurationMs = (int)(fields.Number(12) ?? Ms(timeFields?.Number(2), timescale)),
        };
    }
}
