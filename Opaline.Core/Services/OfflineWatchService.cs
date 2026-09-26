using System.Text.Json;
using Opaline.Core.Api;
using Opaline.Core.Models;

namespace Opaline.Core.Services;

/// <summary>
/// iOS OfflineWatchService: network first; on failure serve saved page.json
/// and related rail from other on-device downloads.
/// </summary>
public sealed class OfflineWatchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly InnertubeClient _client;
    private readonly IDownloadService _downloads;

    public OfflineWatchService(InnertubeClient client, IDownloadService downloads)
    {
        _client = client;
        _downloads = downloads;
    }

    public async Task<(WatchPage Page, string? LocalMediaPath, bool ServedOffline)> GetWatchAsync(
        string videoId, CancellationToken ct = default)
    {
        try
        {
            var page = await _client.GetWatchPageAsync(videoId, ct).ConfigureAwait(false);
            return (page, _downloads.TryGetLocalMediaPath(videoId), false);
        }
        catch
        {
            var offline = TryLoadOffline(videoId);
            if (offline is null)
                throw;
            return offline.Value;
        }
    }

    public (WatchPage Page, string? LocalMediaPath, bool ServedOffline)? TryLoadOffline(string videoId)
    {
        var local = _downloads.TryGetLocalMediaPath(videoId);
        if (string.IsNullOrEmpty(local) || !File.Exists(local))
            return null;

        var snapshot = _downloads.TryLoadWatchSnapshot(videoId);
        var related = BuildLocalRelated(videoId);
        WatchPage page;
        if (snapshot is not null)
        {
            page = snapshot.ToWatchPage(related);
        }
        else
        {
            var title = _downloads.ListDownloads()
                .FirstOrDefault(d => d.VideoId == videoId)?.Title ?? videoId;
            page = new WatchPage
            {
                Video = new Video { Id = videoId, Title = title },
                Streams = Array.Empty<StreamInfo>(),
                RelatedVideos = related
            };
        }

        // Local progressive file as only stream
        page = new WatchPage
        {
            Video = page.Video,
            Streams = new[]
            {
                new StreamInfo
                {
                    Url = local!,
                    MimeType = "video/mp4",
                    QualityLabel = "Offline"
                }
            },
            RelatedVideos = page.RelatedVideos,
            LikeCount = page.LikeCount,
            DislikeCount = page.DislikeCount
        };
        return (page, local, true);
    }

    /// <summary>Other downloads on device as the related rail (iOS offlineShaped).</summary>
    public IReadOnlyList<Video> BuildLocalRelated(string currentVideoId)
    {
        return _downloads.ListDownloads()
            .Where(d => d.VideoId != currentVideoId && File.Exists(d.FilePath))
            .Select(d =>
            {
                var snap = _downloads.TryLoadWatchSnapshot(d.VideoId);
                if (snap is not null)
                    return snap.ToWatchPage().Video;
                return new Video
                {
                    Id = d.VideoId,
                    Title = d.Title,
                    ThumbnailUrl = null
                };
            })
            .ToList();
    }

    public static void WriteSnapshot(string root, OfflineWatchSnapshot snapshot)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{snapshot.VideoId}.watch.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOpts));
    }

    public static OfflineWatchSnapshot? ReadSnapshot(string root, string videoId)
    {
        var path = Path.Combine(root, $"{videoId}.watch.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<OfflineWatchSnapshot>(File.ReadAllText(path), JsonOpts);
        }
        catch { return null; }
    }
}
