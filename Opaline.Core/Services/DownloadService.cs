using System.Net.Http.Headers;
using Opaline.Core.Models;

namespace Opaline.Core.Services;

public interface IDownloadService
{
    string DownloadFolder { get; }
    Task<string> DownloadVideoAsync(Video video, string streamUrl, IProgress<double>? progress = null, CancellationToken ct = default);
    IReadOnlyList<DownloadedItem> ListDownloads();
    void DeleteDownload(string videoId);
    string? TryGetLocalMediaPath(string videoId);
    bool HasOffline(string videoId);
    void SaveWatchSnapshot(OfflineWatchSnapshot snapshot);
    OfflineWatchSnapshot? TryLoadWatchSnapshot(string videoId);
}

public sealed class DownloadedItem
{
    public string VideoId { get; init; } = "";
    public string Title { get; init; } = "";
    public string FilePath { get; init; } = "";
    public long FileSize { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}

/// <summary>Simple progressive-stream offline download to local folder.</summary>
public sealed class DownloadService : IDownloadService
{
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly string _indexPath;

    public DownloadService(HttpClient http)
    {
        _http = http;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opaline", "Downloads");
        Directory.CreateDirectory(_root);
        _indexPath = Path.Combine(_root, "index.json");
    }

    public string DownloadFolder => _root;

    public async Task<string> DownloadVideoAsync(
        Video video, string streamUrl, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var safe = string.Concat(video.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safe.Length > 80) safe = safe[..80];
        var file = Path.Combine(_root, $"{video.Id}_{safe}.mp4");

        using var resp = await _http.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(file);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total > 0) progress?.Report(read / (double)total);
        }

        UpsertIndex(new DownloadedItem
        {
            VideoId = video.Id,
            Title = video.Title,
            FilePath = file,
            FileSize = read,
            SavedAt = DateTimeOffset.UtcNow
        });
        return file;
    }

    public IReadOnlyList<DownloadedItem> ListDownloads()
    {
        if (!File.Exists(_indexPath)) return Array.Empty<DownloadedItem>();
        try
        {
            var json = File.ReadAllText(_indexPath);
            return System.Text.Json.JsonSerializer.Deserialize<List<DownloadedItem>>(json)
                   ?? new List<DownloadedItem>();
        }
        catch { return Array.Empty<DownloadedItem>(); }
    }


    public void SaveWatchPage(string videoId, object watchPagePayload)
    {
        try
        {
            var path = Path.Combine(_root, $"{videoId}.watch.json");
            var json = System.Text.Json.JsonSerializer.Serialize(watchPagePayload);
            File.WriteAllText(path, json);
        }
        catch { /* ignore */ }
    }

    public string? TryGetLocalMediaPath(string videoId)
    {
        return ListDownloads().FirstOrDefault(i => i.VideoId == videoId)?.FilePath;
    }

    public bool HasOffline(string videoId)
        => !string.IsNullOrEmpty(TryGetLocalMediaPath(videoId));

    public void DeleteDownload(string videoId)
    {
        var items = ListDownloads().ToList();
        var match = items.Where(i => i.VideoId == videoId).ToList();
        foreach (var m in match)
        {
            try { if (File.Exists(m.FilePath)) File.Delete(m.FilePath); } catch { /* ignore */ }
            try
            {
                var meta = Path.Combine(_root, $"{m.VideoId}.watch.json");
                if (File.Exists(meta)) File.Delete(meta);
            }
            catch { /* ignore */ }
            items.Remove(m);
        }
        File.WriteAllText(_indexPath, System.Text.Json.JsonSerializer.Serialize(items));
    }

    private void UpsertIndex(DownloadedItem item)
    {
        var items = ListDownloads().Where(i => i.VideoId != item.VideoId).ToList();
        items.Insert(0, item);
        File.WriteAllText(_indexPath, System.Text.Json.JsonSerializer.Serialize(items));
    }
}