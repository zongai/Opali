using System.Text.Json;
using Opaline.Core.Models;

namespace Opaline.Core.Storage;

/// <summary>Local watch history + Watch Later list.</summary>
public sealed class WatchHistoryStore
{
    private readonly string _historyPath;
    private readonly string _laterPath;
    private readonly object _lock = new();

    public WatchHistoryStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Opaline");
        Directory.CreateDirectory(dir);
        _historyPath = Path.Combine(dir, "history.json");
        _laterPath = Path.Combine(dir, "watch_later.json");
    }

    public IReadOnlyList<Video> GetHistory(int limit = 200)
    {
        lock (_lock)
        {
            var list = Load(_historyPath);
            return list.Take(limit).ToList();
        }
    }

    public void AddToHistory(Video video)
    {
        lock (_lock)
        {
            var list = Load(_historyPath);
            list.RemoveAll(v => v.Id == video.Id);
            list.Insert(0, video);
            if (list.Count > 500) list.RemoveRange(500, list.Count - 500);
            Save(_historyPath, list);
        }
    }

    public IReadOnlyList<Video> GetWatchLater()
    {
        lock (_lock) return Load(_laterPath);
    }

    public void AddWatchLater(Video video)
    {
        lock (_lock)
        {
            var list = Load(_laterPath);
            if (list.Any(v => v.Id == video.Id)) return;
            list.Insert(0, video);
            Save(_laterPath, list);
        }
    }

    public void RemoveWatchLater(string videoId)
    {
        lock (_lock)
        {
            var list = Load(_laterPath);
            list.RemoveAll(v => v.Id == videoId);
            Save(_laterPath, list);
        }
    }

    public void ClearHistory()
    {
        lock (_lock) Save(_historyPath, new List<Video>());
    }

    private static List<Video> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<Video>();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Video>>(json) ?? new List<Video>();
        }
        catch
        {
            return new List<Video>();
        }
    }

    private static void Save(string path, List<Video> list)
    {
        var json = JsonSerializer.Serialize(list);
        File.WriteAllText(path, json);
    }
}
