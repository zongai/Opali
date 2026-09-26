using System.Globalization;
using System.Text;

namespace Opaline.Core.Playback.Hls;

/// <summary>HLS playlist builders from SIDX segments (iOS HLSGenerator).</summary>
public static class HlsPlaylistGenerator
{
    public static string MediaPlaylist(
        string mediaUrl,
        int initBytes,
        long dataStartOffset,
        IReadOnlyList<SidxSegment> segments,
        double? startAt = null)
    {
        var maxDur = segments.Count > 0 ? segments.Max(s => s.Duration) : 5;
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:7");
        sb.AppendLine($"#EXT-X-TARGETDURATION:{(int)Math.Ceiling(maxDur)}");
        sb.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
        if (startAt is > 0)
            sb.AppendLine(FormattableString.Invariant($"#EXT-X-START:TIME-OFFSET={startAt.Value:0.###}"));
        sb.AppendLine($"#EXT-X-MAP:URI=\"{mediaUrl}\",BYTERANGE=\"{initBytes}@0\"");
        foreach (var seg in segments)
        {
            var off = dataStartOffset + seg.Offset;
            sb.AppendLine(FormattableString.Invariant($"#EXTINF:{seg.Duration:0.000},"));
            sb.AppendLine($"#EXT-X-BYTERANGE:{seg.Size}@{off}");
            sb.AppendLine(mediaUrl);
        }
        sb.AppendLine("#EXT-X-ENDLIST");
        return sb.ToString();
    }

    public static string MasterPlaylist(
        int bandwidth,
        string codecs,
        string resolution,
        string videoPlaylistUri,
        string audioPlaylistUri)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXT-X-VERSION:7");
        sb.AppendLine("#EXT-X-INDEPENDENT-SEGMENTS");
        sb.AppendLine(
            "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"audio\",NAME=\"Main\","
            + $"DEFAULT=YES,AUTOSELECT=YES,URI=\"{audioPlaylistUri}\"");
        sb.AppendLine(
            $"#EXT-X-STREAM-INF:BANDWIDTH={bandwidth},CODECS=\"{codecs}\","
            + $"RESOLUTION={resolution},AUDIO=\"audio\"");
        sb.AppendLine(videoPlaylistUri);
        return sb.ToString();
    }

    public static int PeakBitrate(IReadOnlyList<SidxSegment> segments, int fallback)
    {
        var peak = 0;
        foreach (var s in segments)
        {
            if (s.Duration <= 0) continue;
            peak = Math.Max(peak, (int)(s.Size * 8 / s.Duration));
        }
        return peak > 0 ? peak : fallback;
    }
}
