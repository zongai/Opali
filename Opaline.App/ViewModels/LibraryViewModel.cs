using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Services;
using Opaline.Core.Storage;

namespace Opaline.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly WatchHistoryStore _store;
    private readonly IYouTubeService _yt;
    private readonly IDownloadService _downloads;

    public ObservableCollection<Video> History { get; } = new();
    public ObservableCollection<Video> WatchLater { get; } = new();
    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<DownloadedItem> Downloads { get; } = new();

    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private string? statusMessage;

    public LibraryViewModel(WatchHistoryStore store, IYouTubeService yt, IDownloadService downloads)
    {
        _store = store;
        _yt = yt;
        _downloads = downloads;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        History.Clear();
        foreach (var v in _store.GetHistory()) History.Add(v);
        WatchLater.Clear();
        foreach (var v in _store.GetWatchLater()) WatchLater.Add(v);
        Downloads.Clear();
        foreach (var d in _downloads.ListDownloads()) Downloads.Add(d);

        Playlists.Clear();
        try
        {
            var pls = await _yt.GetLibraryPlaylistsAsync();
            foreach (var p in pls) Playlists.Add(p);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Playlists: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearHistory()
    {
        _store.ClearHistory();
        History.Clear();
    }

    [RelayCommand]
    public void RemoveWatchLater(string? videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return;
        _store.RemoveWatchLater(videoId);
        var item = WatchLater.FirstOrDefault(v => v.Id == videoId);
        if (item is not null) WatchLater.Remove(item);
    }

    [RelayCommand]
    public void DeleteDownload(string? videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return;
        _downloads.DeleteDownload(videoId);
        var item = Downloads.FirstOrDefault(d => d.VideoId == videoId);
        if (item is not null) Downloads.Remove(item);
    }
}
