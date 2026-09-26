namespace Opaline.Core.Playback.Sabr;

public readonly record struct UmpPart(int Type, byte[] Payload);

public enum UmpPartType
{
    MediaHeader = 20,
    Media = 21,
    MediaEnd = 22,
    NextRequestPolicy = 35,
    FormatInitializationMetadata = 42,
    SabrRedirect = 43,
    SabrError = 44,
    SabrSeek = 45,
    ReloadPlayerResponse = 46,
    EndOfTrack = 62,
}

/// <summary>Incremental UMP (type, size, payload) reader — iOS UMPReader.</summary>
public sealed class UmpReader
{
    private readonly List<byte> _buffer = new();

    public void Append(ReadOnlySpan<byte> chunk)
    {
        foreach (var b in chunk) _buffer.Add(b);
    }

    public void Append(byte[] chunk) => Append(chunk.AsSpan());

    public List<UmpPart> ReadParts()
    {
        var parts = new List<UmpPart>();
        var data = _buffer.ToArray();
        var offset = 0;
        while (true)
        {
            if (!SabrProtobuf.TryReadUmpVarint(data, offset, out var type, out var afterType))
                break;
            if (!SabrProtobuf.TryReadUmpVarint(data, afterType, out var size, out var afterSize))
                break;
            if (size < 0 || afterSize + size > data.Length)
                break;
            var payload = new byte[size];
            Buffer.BlockCopy(data, afterSize, payload, 0, size);
            parts.Add(new UmpPart(type, payload));
            offset = afterSize + size;
        }
        if (offset > 0)
            _buffer.RemoveRange(0, offset);
        return parts;
    }
}
