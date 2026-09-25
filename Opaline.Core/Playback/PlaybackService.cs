using Opaline.Core.Models;

namespace Opaline.Core.Playback;

public sealed class PlaybackService
{
    private readonly StreamUrlResolver _resolver;

    public PlaybackService(StreamUrlResolver resolver) => _resolver = resolver;

    public Task<ResolvedStream?> ResolveAsync(
        WatchPage page,
        int maxHeight = 1080,
        CancellationToken ct = default)
        => _resolver.ResolveAsync(page, maxHeight, preferAdaptive: false, ct);

    public StreamInfo? SelectBestProgressive(WatchPage page, int maxHeight = 1080)
        => StreamUrlResolver.SelectBestProgressive(page, maxHeight);

    public (StreamInfo? Video, StreamInfo? Audio) SelectBestAdaptive(WatchPage page, int maxHeight = 1080)
        => StreamUrlResolver.SelectBestAdaptive(page, maxHeight);

    public string? ResolvePlayableUrl(WatchPage page, int maxHeight = 1080)
    {
        var progressive = SelectBestProgressive(page, maxHeight);
        if (progressive is not null) return progressive.Url;
        var (video, _) = SelectBestAdaptive(page, maxHeight);
        return video?.Url;
    }
}
