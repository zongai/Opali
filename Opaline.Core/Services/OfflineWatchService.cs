using Opaline.Core.Api;
using Opaline.Core.Models;

namespace Opaline.Core.Services;

/// <summary>
/// iOS OfflineWatchService: network first, then downloaded media for playback.
/// Full WatchPage JSON offline shape is partial — local file path is primary.
/// </summary>
public sealed class OfflineWatchService
{
    private readonly InnertubeClient _client;
    private readonly IDownloadService _downloads;

    public OfflineWatchService(InnertubeClient client, IDownloadService downloads)
    {
        _client = client;
        _downloads = downloads;
    }

    public async Task<(WatchPage? Page, string? LocalMediaPath)> GetWatchAsync(
        string videoId, CancellationToken ct = default)
    {
        try
        {
            var page = await _client.GetWatchPageAsync(videoId, ct).ConfigureAwait(false);
            // Persist slim metadata for offline listing when user downloads later
            return (page, _downloads.TryGetLocalMediaPath(videoId));
        }
        catch
        {
            var local = _downloads.TryGetLocalMediaPath(videoId);
            if (!string.IsNullOrEmpty(local))
            {
                // Synthetic minimal page for offline playback
                var page = new WatchPage
                {
                    Video = new Video { Id = videoId, Title = videoId },
                    Streams = new[]
                    {
                        new StreamInfo
                        {
                            Url = local!,
                            MimeType = "video/mp4",
                            QualityLabel = "Offline"
                        }
                    }
                };
                return (page, local);
            }
            throw;
        }
    }
}
