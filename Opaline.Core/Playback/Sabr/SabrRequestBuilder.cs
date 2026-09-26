namespace Opaline.Core.Playback.Sabr;

public enum SabrClientKind
{
    Android,
    Tv
}

/// <summary>Builds VideoPlaybackAbrRequest bodies (iOS SABRRequest + TVClient).</summary>
public static class SabrRequestBuilder
{
    public static byte[] ClientInfo(SabrClientKind kind) => kind switch
    {
        SabrClientKind.Tv => ClientInfoTv(),
        _ => ClientInfoAndroid()
    };

    public static byte[] ClientInfoAndroid(string version = "21.26.364") =>
        SabrProtobuf.Concat(
            SabrProtobuf.Int(16, 3),
            SabrProtobuf.String(17, version),
            SabrProtobuf.String(18, "Android"),
            SabrProtobuf.String(19, "11"),
            CommonTail());

    public static byte[] ClientInfoTv(string version = "7.20260311.12.00") =>
        SabrProtobuf.Concat(
            SabrProtobuf.Int(16, 7),
            SabrProtobuf.String(17, version),
            SabrProtobuf.String(18, "Cobalt"),
            SabrProtobuf.String(19, "22.lts.3.306369-gold"),
            CommonTail());

    private static byte[] CommonTail() => SabrProtobuf.Concat(
        SabrProtobuf.String(21, "en-US"),
        SabrProtobuf.String(22, "US"),
        SabrProtobuf.Int(37, 1920),
        SabrProtobuf.Int(38, 1080),
        SabrProtobuf.Int(41, 1),
        SabrProtobuf.Int(46, 2),
        SabrProtobuf.Int(55, 1920),
        SabrProtobuf.Int(56, 1080),
        SabrProtobuf.Float(65, 1.0f));

    public static byte[] AbrState(SabrClientKind kind, SabrPlayerState state) =>
        kind == SabrClientKind.Tv ? AbrStateTv(state) : AbrStateAndroid(state);

    public static byte[] AbrStateAndroid(SabrPlayerState state)
    {
        var body = SabrProtobuf.Int(28, state.PlayerMs);
        if (state.Video.Height is int h && h > 0)
            body = SabrProtobuf.Concat(body, SabrProtobuf.Int(21, h));
        body = SabrProtobuf.Concat(
            body,
            SabrProtobuf.Bool(22, false),
            SabrProtobuf.Int(34, 1),
            SabrProtobuf.Float(35, 1.0f),
            SabrProtobuf.Int(40, (int)state.Wants));
        if (!string.IsNullOrEmpty(state.AudioTrackId))
            body = SabrProtobuf.Concat(body, SabrProtobuf.String(69, state.AudioTrackId!));
        return body;
    }

    /// <summary>
    /// Field-for-field TVHTML5 / Cobalt sabrAbrState (iOS TVClient, captured live).
    /// </summary>
    public static byte[] AbrStateTv(SabrPlayerState state)
    {
        var body = SabrProtobuf.Concat(
            SabrProtobuf.Int(18, 1920),   // clientViewportWidth
            SabrProtobuf.Int(19, 1080),  // clientViewportHeight
            SabrProtobuf.Int(21, 0));    // stickyResolution
        if (state.Video.Bitrate is long br && br > 0)
            body = SabrProtobuf.Concat(body, SabrProtobuf.Int(23, br)); // bandwidthEstimate
        body = SabrProtobuf.Concat(
            body,
            SabrProtobuf.Int(28, state.PlayerMs),
            SabrProtobuf.Int(34, 0),              // visibility
            SabrProtobuf.Bool(46, true),          // drcEnabled
            SabrProtobuf.Bool(58, false),         // preferVp9
            SabrProtobuf.Int(59, DecodeCeiling())); // av1QualityThreshold
        if (!string.IsNullOrEmpty(state.AudioTrackId))
            body = SabrProtobuf.Concat(body, SabrProtobuf.String(69, state.AudioTrackId!));
        body = SabrProtobuf.Concat(
            body,
            SabrProtobuf.Bytes(72, DecodeCeilings()),
            SabrProtobuf.Int(73, 2),
            SabrProtobuf.Bytes(79, TrackAuthorization()),
            SabrProtobuf.Int(80, 1),
            SabrProtobuf.Int(85, 1));
        return body;
    }

    private static int DecodeCeiling() => 1080;

    private static byte[] DecodeCeilings() => SabrProtobuf.Concat(
        SabrProtobuf.Int(1, 0),
        SabrProtobuf.Int(2, DecodeCeiling()),
        SabrProtobuf.Int(3, 0),
        SabrProtobuf.Int(4, 0),
        SabrProtobuf.Int(5, DecodeCeiling()),
        SabrProtobuf.Int(6, 0));

    /// <summary>Audio + SDR video + HDR video authorization entries.</summary>
    private static byte[] TrackAuthorization()
    {
        static byte[] Entry(int trackType, bool hdr)
        {
            var format = SabrProtobuf.Concat(
                SabrProtobuf.Int(1, trackType),
                SabrProtobuf.Bool(2, hdr));
            return SabrProtobuf.Bytes(1, format);
        }
        return SabrProtobuf.Concat(Entry(1, false), Entry(2, false), Entry(2, true));
    }

    public static byte[] FormatId(SabrFormatInfo format)
    {
        var data = SabrProtobuf.Int(1, format.Itag);
        if (long.TryParse(format.LastModified, out var lm))
            data = SabrProtobuf.Concat(data, SabrProtobuf.Int(2, lm));
        if (!string.IsNullOrEmpty(format.Xtags))
            data = SabrProtobuf.Concat(data, SabrProtobuf.String(3, format.Xtags!));
        return data;
    }

    public static byte[] StreamerContext(byte[] clientInfo, byte[]? poToken, byte[]? playbackCookie)
    {
        var ctx = SabrProtobuf.Bytes(1, clientInfo);
        if (poToken is { Length: > 0 })
            ctx = SabrProtobuf.Concat(ctx, SabrProtobuf.Bytes(2, poToken));
        if (playbackCookie is { Length: > 0 })
            ctx = SabrProtobuf.Concat(ctx, SabrProtobuf.Bytes(3, playbackCookie));
        return ctx;
    }

    public static byte[] Startup(
        byte[] ustreamerConfig,
        SabrFormatInfo audio,
        SabrFormatInfo video,
        SabrClientKind clientKind = SabrClientKind.Tv,
        byte[]? poToken = null,
        byte[]? playbackCookie = null)
    {
        var clientInfo = ClientInfo(clientKind);
        var state = AbrState(clientKind, new SabrPlayerState
        {
            Video = video,
            AudioTrackId = audio.AudioTrackId,
            Wants = SabrTrackWants.Both,
            PlayerMs = 0
        });
        return SabrProtobuf.Concat(
            SabrProtobuf.Bytes(1, state),
            SabrProtobuf.Bytes(5, ustreamerConfig),
            SabrProtobuf.Bytes(16, FormatId(audio)),
            SabrProtobuf.Bytes(17, FormatId(video)),
            SabrProtobuf.Bytes(19, StreamerContext(clientInfo, poToken, playbackCookie)));
    }

    public static byte[] Segment(
        byte[] ustreamerConfig,
        SabrFormatInfo format,
        SabrFormatInfo other,
        bool isInit,
        int playerMs,
        SabrBufferedRange? held,
        SabrClientKind clientKind = SabrClientKind.Tv,
        byte[]? poToken = null,
        byte[]? playbackCookie = null)
    {
        var clientInfo = ClientInfo(clientKind);
        var isVideo = (format.Height ?? 0) > 0;
        var video = isVideo ? format : other;
        var audio = isVideo ? other : format;
        var wants = isVideo ? SabrTrackWants.Video : SabrTrackWants.Audio;
        var state = AbrState(clientKind, new SabrPlayerState
        {
            Video = video,
            AudioTrackId = audio.AudioTrackId,
            Wants = wants,
            PlayerMs = playerMs
        });
        var body = SabrProtobuf.Concat(
            SabrProtobuf.Bytes(1, state),
            SabrProtobuf.Bytes(2, FormatId(other)));
        if (!isInit)
            body = SabrProtobuf.Concat(body, SabrProtobuf.Bytes(2, FormatId(format)));
        if (held is not null && !isInit)
            body = SabrProtobuf.Concat(body, SabrProtobuf.Bytes(3, BufferedRange(held)));
        body = SabrProtobuf.Concat(
            body,
            SabrProtobuf.Bytes(5, ustreamerConfig),
            SabrProtobuf.Bytes(19, StreamerContext(clientInfo, poToken, playbackCookie)));
        return body;
    }

    private static byte[] BufferedRange(SabrBufferedRange d)
    {
        var range = SabrProtobuf.Concat(
            SabrProtobuf.Bytes(1, FormatId(d.Format)),
            SabrProtobuf.Int(2, d.StartMs),
            SabrProtobuf.Int(3, d.DurationMs),
            SabrProtobuf.Int(4, d.StartSequence),
            SabrProtobuf.Int(5, d.EndSequence));
        var timeRange = SabrProtobuf.Concat(
            SabrProtobuf.Int(1, d.StartMs),
            SabrProtobuf.Int(2, d.DurationMs),
            SabrProtobuf.Int(3, d.Timescale));
        return SabrProtobuf.Concat(range, SabrProtobuf.Bytes(6, timeRange));
    }
}
