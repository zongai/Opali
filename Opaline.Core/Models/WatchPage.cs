namespace Opaline.Core.Models;

/// <summary>
/// Full watch page payload: metadata + streams + manifests + related.
/// </summary>
public sealed class WatchPage
{
    public Video Video { get; init; }
    public IReadOnlyList<StreamInfo> Streams { get; init; }
    public IReadOnlyList<Video>? RelatedVideos { get; init; }
    public string? LikeCount { get; init; }
    public string? DislikeCount { get; init; }
    public bool IsLiked { get; set; }
    public bool IsDisliked { get; set; }
    public string? ContinuationToken { get; init; }

    /// <summary>Server-provided HLS master playlist (preferred for adaptive).</summary>
    public string? HlsManifestUrl { get; init; }
    public string? DashManifestUrl { get; init; }
    /// <summary>SABR UMP endpoint from streamingData.serverAbrStreamingUrl.</summary>
    public string? ServerAbrStreamingUrl { get; init; }
    /// <summary>Base64 videoPlaybackUstreamerConfig for SABR request body.</summary>
    public string? VideoPlaybackUstreamerConfig { get; init; }
    /// <summary>Fallback ustreamer blob (onesie) when playback config absent.</summary>
    public string? OnesieUstreamerConfig { get; init; }

    /// <summary>Available caption / subtitle tracks.</summary>
    public IReadOnlyList<CaptionTrack> CaptionTracks { get; init; } = Array.Empty<CaptionTrack>();

    /// <summary>Video quality options (non-audio).</summary>
    public IReadOnlyList<StreamInfo> VideoQualities { get; init; } = Array.Empty<StreamInfo>();

    /// <summary>Audio-only streams.</summary>
    public IReadOnlyList<StreamInfo> AudioTracks { get; init; } = Array.Empty<StreamInfo>();
}

public sealed class StreamInfo
{
    public string Url { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Fps { get; init; }
    public long? Bitrate { get; init; }
    public string? QualityLabel { get; init; }
    public bool IsAudioOnly { get; init; }
    public bool IsVideoOnly { get; init; }
    public string? Codecs { get; init; }
    public long? ContentLength { get; init; }
    public int? Itag { get; init; }

    /// <summary>Encrypted signature challenge from signatureCipher (field <c>s</c>).</summary>
    public string? SigChallenge { get; init; }

    /// <summary>Query parameter name for the solved signature (usually <c>sig</c>).</summary>
    public string? SigParam { get; init; }

    /// <summary>Raw n-parameter from the URL, if present (pre-solve).</summary>
    public string? NParam { get; init; }
    public string? LastModified { get; init; }
    public int InitRangeStart { get; init; }
    public int InitRangeEnd { get; init; }
    public int IndexRangeStart { get; init; }
    public int IndexRangeEnd { get; init; }

    public string DisplayLabel => QualityLabel
        ?? (Height is { } h ? $"{h}p" : (IsAudioOnly ? "Audio" : "Unknown"));
}

public sealed class CommentThread
{
    public string Id { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string? AuthorAvatarUrl { get; init; }
    public string Text { get; init; } = string.Empty;
    public long? LikeCount { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? PublishedTime { get; init; }
    public int ReplyCount { get; init; }
    public IReadOnlyList<CommentThread>? Replies { get; init; }
    public string? TranslatedText { get; set; }
}


public sealed class CommentsPage
{
    public IReadOnlyList<CommentThread> Comments { get; init; } = Array.Empty<CommentThread>();
    public string? ContinuationToken { get; init; }
    public long? TotalCount { get; init; }
}
