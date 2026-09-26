namespace Opaline.Core.Playback.Sabr;

/// <summary>Minimal protobuf writer/reader for SABR (iOS Protobuf.swift).</summary>
public static class SabrProtobuf
{
    public abstract record Value
    {
        public sealed record Number(long N) : Value;
        public sealed record Bytes(byte[] Data) : Value;
    }

    public static byte[] Varint(ulong value)
    {
        var ms = new MemoryStream(8);
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            ms.WriteByte(b);
        } while (value != 0);
        return ms.ToArray();
    }

    public static byte[] Tag(int field, int wire) => Varint((ulong)((field << 3) | wire));
    public static byte[] Int(int field, long value) => Concat(Tag(field, 0), Varint((ulong)Math.Max(value, 0)));
    public static byte[] Bool(int field, bool value) => Int(field, value ? 1 : 0);
    public static byte[] Float(int field, float value)
    {
        var bits = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bits);
        return Concat(Tag(field, 5), bits);
    }
    public static byte[] Bytes(int field, byte[] value)
        => Concat(Tag(field, 2), Varint((ulong)value.Length), value);
    public static byte[] String(int field, string value)
        => Bytes(field, System.Text.Encoding.UTF8.GetBytes(value));

    public static byte[] Concat(params byte[][] parts)
    {
        var n = parts.Sum(p => p.Length);
        var buf = new byte[n];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, buf, o, p.Length);
            o += p.Length;
        }
        return buf;
    }

    public static Dictionary<int, List<Value>> Parse(byte[] data)
    {
        var fields = new Dictionary<int, List<Value>>();
        var offset = 0;
        while (offset < data.Length)
        {
            if (!TryReadPbVarint(data, offset, out var key, out var afterKey)) break;
            if (!TryReadValue(data, afterKey, (int)(key & 7), out var value, out var next)) break;
            var field = (int)(key >> 3);
            if (!fields.TryGetValue(field, out var list))
            {
                list = new List<Value>();
                fields[field] = list;
            }
            list.Add(value);
            offset = next;
        }
        return fields;
    }

    public static long? Number(this Dictionary<int, List<Value>> fields, int field)
    {
        if (!fields.TryGetValue(field, out var list) || list.Count == 0) return null;
        return list[0] is Value.Number n ? n.N : null;
    }

    public static byte[]? Data(this Dictionary<int, List<Value>> fields, int field)
    {
        if (!fields.TryGetValue(field, out var list) || list.Count == 0) return null;
        return list[0] is Value.Bytes b ? b.Data : null;
    }

    public static string? Str(this Dictionary<int, List<Value>> fields, int field)
    {
        var d = fields.Data(field);
        return d is null ? null : System.Text.Encoding.UTF8.GetString(d);
    }

    private static bool TryReadPbVarint(byte[] data, int offset, out ulong value, out int next)
    {
        value = 0; next = offset;
        int shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) { next = offset; return true; }
            shift += 7;
        }
        return false;
    }

    private static bool TryReadValue(byte[] data, int offset, int wire, out Value value, out int next)
    {
        value = new Value.Number(0); next = offset;
        switch (wire)
        {
            case 0:
                if (!TryReadPbVarint(data, offset, out var n, out next)) return false;
                value = new Value.Number((long)n);
                return true;
            case 1:
                if (offset + 8 > data.Length) return false;
                value = new Value.Bytes(data.AsSpan(offset, 8).ToArray());
                next = offset + 8;
                return true;
            case 2:
                if (!TryReadPbVarint(data, offset, out var len, out var after)) return false;
                if (after + (int)len > data.Length) return false;
                value = new Value.Bytes(data.AsSpan(after, (int)len).ToArray());
                next = after + (int)len;
                return true;
            case 5:
                if (offset + 4 > data.Length) return false;
                value = new Value.Bytes(data.AsSpan(offset, 4).ToArray());
                next = offset + 4;
                return true;
            default:
                return false;
        }
    }

    /// <summary>UMP length varint (not protobuf): high bits of first byte encode width.</summary>
    public static bool TryReadUmpVarint(byte[] data, int offset, out int value, out int next)
    {
        value = 0; next = offset;
        if (offset < 0 || offset >= data.Length) return false;
        var first = data[offset];
        int length = first switch
        {
            < 128 => 1,
            < 192 => 2,
            < 224 => 3,
            < 240 => 4,
            _ => 5
        };
        if (offset + length > data.Length) return false;
        if (length == 5)
        {
            value = (int)ReadLe(data, offset + 1, 4);
            next = offset + 5;
            return true;
        }
        var head = first & (0xFF >> length);
        var tail = ReadLe(data, offset + 1, length - 1);
        value = (int)((ulong)head + (tail << (8 - length)));
        next = offset + length;
        return true;
    }

    private static ulong ReadLe(byte[] data, int offset, int count)
    {
        ulong v = 0;
        for (var i = 0; i < count; i++)
            v |= (ulong)data[offset + i] << (8 * i);
        return v;
    }
}
