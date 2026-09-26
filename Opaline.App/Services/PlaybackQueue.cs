using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Opaline.Core.Models;

namespace Opaline.App.Services;

/// <summary>Simple in-app queue + mini-player state (iOS PlaybackQueue / MiniPlayerBar).</summary>
public sealed partial class PlaybackQueue : ObservableObject
{
    public ObservableCollection<Video> Items { get; } = new();

    [ObservableProperty] private Video? nowPlaying;
    [ObservableProperty] private bool isMiniPlayerVisible;
    [ObservableProperty] private bool isPaused;

    public void PlayNow(Video video)
    {
        NowPlaying = video;
        IsMiniPlayerVisible = true;
        IsPaused = false;
        if (!Items.Any(v => v.Id == video.Id))
            Items.Insert(0, video);
    }

    public void Enqueue(Video video)
    {
        if (Items.Any(v => v.Id == video.Id)) return;
        Items.Add(video);
    }

    public Video? PlayNext()
    {
        if (NowPlaying is null)
        {
            if (Items.Count == 0) return null;
            PlayNow(Items[0]);
            return NowPlaying;
        }
        var idx = Items.ToList().FindIndex(v => v.Id == NowPlaying.Id);
        if (idx < 0 || idx + 1 >= Items.Count)
        {
            IsMiniPlayerVisible = false;
            return null;
        }
        PlayNow(Items[idx + 1]);
        return NowPlaying;
    }

    public void Clear()
    {
        Items.Clear();
        NowPlaying = null;
        IsMiniPlayerVisible = false;
    }
}
