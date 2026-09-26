namespace Opaline.Core.Playback.Sabr;

/// <summary>nextRequestPolicy (UMP 35): backoff + playback cookie for next request.</summary>
public sealed class SabrPolicy
{
    public int BackoffMs { get; init; }
    public byte[]? PlaybackCookie { get; init; }
}

/// <summary>Collects MEDIA parts for one segment request (iOS SABRSegmentCollector).</summary>
public sealed class SabrSegmentCollector
{
    private readonly UmpReader _reader = new();
    private readonly List<byte[]> _chunks = new();
    private MediaHeader? _header;
    private bool _done;

    public int Received { get; private set; }
    public MediaHeader? Header => _header;
    public List<int> SeenTypes { get; } = new();
    public SabrBufferedRange? DeliveredRange { get; private set; }
    public string? RedirectUrl { get; private set; }
    public string? ErrorDetail { get; private set; }
    public SabrPolicy? Policy { get; private set; }
    public bool IsDone => _done;

    public byte[]? Segment
    {
        get
        {
            if (_chunks.Count == 0) return null;
            if (_chunks.Count == 1) return _chunks[0];
            var n = _chunks.Sum(c => c.Length);
            var buf = new byte[n];
            var o = 0;
            foreach (var c in _chunks)
            {
                Buffer.BlockCopy(c, 0, buf, o, c.Length);
                o += c.Length;
            }
            return buf;
        }
    }

    public void Append(byte[] chunk)
    {
        Received += chunk.Length;
        _reader.Append(chunk);
        foreach (var part in _reader.ReadParts())
        {
            if (_done) break;
            Handle(part);
        }
    }

    private void Handle(UmpPart part)
    {
        SeenTypes.Add(part.Type);
        switch ((UmpPartType)part.Type)
        {
            case UmpPartType.MediaHeader:
                _header = MediaHeader.TryParse(part.Payload);
                break;
            case UmpPartType.Media:
                if (part.Payload.Length > 0)
                    _chunks.Add(part.Payload);
                break;
            case UmpPartType.MediaEnd:
                Finish();
                break;
            case UmpPartType.NextRequestPolicy:
                NotePolicy(part.Payload);
                break;
            case UmpPartType.SabrRedirect:
                // payload often carries url string in protobuf field
                var fields = SabrProtobuf.Parse(part.Payload);
                RedirectUrl = fields.Str(1) ?? fields.Str(2);
                break;
            case UmpPartType.SabrError:
                ErrorDetail = SabrProtobuf.Parse(part.Payload).Str(1) ?? "sabr_error";
                _done = true;
                break;
            case UmpPartType.EndOfTrack:
                Finish();
                break;
        }
    }

    private void NotePolicy(byte[] payload)
    {
        var fields = SabrProtobuf.Parse(payload);
        // iOS: backoffMs field 4, playbackCookie field 7
        Policy = new SabrPolicy
        {
            BackoffMs = (int)(fields.Number(4) ?? 0),
            PlaybackCookie = fields.Data(7)
        };
    }

    private void Finish()
    {
        if (_header is not null)
        {
            DeliveredRange = new SabrBufferedRange
            {
                Format = new SabrFormatInfo { Itag = _header.Itag, Xtags = _header.Xtags },
                StartMs = _header.StartMs,
                DurationMs = _header.DurationMs,
                StartSequence = _header.Sequence,
                EndSequence = _header.Sequence,
                Timescale = _header.Timescale
            };
        }
        _done = true;
    }

    /// <summary>Mark complete when stream ends without MEDIA_END (still have media).</summary>
    public void CompleteIfHasMedia()
    {
        if (!_done && _chunks.Count > 0)
            Finish();
    }
}
