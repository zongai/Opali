namespace Opaline.Core.Playback.Hls;

public readonly record struct SidxSegment(long Offset, long Size, double Duration);

/// <summary>ISO BMFF sidx box parser (iOS HLSGenerator+Sidx).</summary>
public static class SidxParser
{
    public static IReadOnlyList<SidxSegment>? Parse(byte[] data)
    {
        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            long boxSize = ReadU32(data, pos);
            var boxType = ReadFourCC(data, pos + 4);
            if (boxSize == 1 && pos + 16 <= data.Length)
                boxSize = (long)ReadU64(data, pos + 8);
            if (boxSize < 8) break;
            if (boxType == "sidx")
            {
                var clamped = (int)Math.Min(boxSize, data.Length - pos);
                return ParseSidxContent(data, pos, clamped);
            }
            pos += (int)boxSize;
        }
        return null;
    }

    private static IReadOnlyList<SidxSegment>? ParseSidxContent(byte[] data, int boxStart, int boxSize)
    {
        var boxEnd = boxStart + boxSize;
        var pos = boxStart + 8;
        if (pos + 4 > boxEnd) return null;
        var version = data[pos];
        pos += 4;
        if (pos + 8 > boxEnd) return null;
        var timescale = ReadU32(data, pos + 4);
        if (timescale == 0) return null;
        pos += 8;
        var timeFieldSize = version == 0 ? 8 : 16;
        if (pos + timeFieldSize > boxEnd) return null;
        pos += timeFieldSize;
        if (pos + 4 > boxEnd) return null;
        pos += 2;
        var refCount = ReadU16(data, pos);
        pos += 2;

        var segments = new List<SidxSegment>(refCount);
        long currentOffset = 0;
        for (var i = 0; i < refCount; i++)
        {
            if (pos + 12 > boxEnd) break;
            var refSize = ReadU32(data, pos) & 0x7FFFFFFF; // clear SAP type bit
            pos += 4;
            var subsegmentDuration = ReadU32(data, pos);
            pos += 4;
            pos += 4; // SAP fields
            var duration = (double)subsegmentDuration / timescale;
            segments.Add(new SidxSegment(currentOffset, refSize, duration));
            currentOffset += refSize;
        }
        return segments.Count > 0 ? segments : null;
    }

    private static uint ReadU32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    private static ulong ReadU64(byte[] d, int o) =>
        ((ulong)ReadU32(d, o) << 32) | ReadU32(d, o + 4);

    private static int ReadU16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static string ReadFourCC(byte[] d, int o) =>
        System.Text.Encoding.ASCII.GetString(d, o, 4);
}
