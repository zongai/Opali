using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Storage;

namespace Opaline.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly WatchHistoryStore _store;

    public ObservableCollection<Video> History { get; } = new();
    public ObservableCollection<Video> WatchLater { get; } = new();

    [ObservableProperty] private int selectedTab; // 0 history, 1 watch later

    public LibraryViewModel(WatchHistoryStore store) => _store = store;

    [RelayCommand]
    public void Load()
    {
        History.Clear();
        foreach (var v in _store.GetHistory())
            History.Add(v);

        WatchLater.Clear();
        foreach (var v in _store.GetWatchLater())
            WatchLater.Add(v);
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
}
