using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class ShortsViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private string? _continuation;

    public ObservableCollection<Video> Shorts { get; } = new();

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private int currentIndex;

    public ShortsViewModel(IYouTubeService yt) => _yt = yt;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var feed = await _yt.GetShortsAsync();
            Shorts.Clear();
            foreach (var item in feed.Items.OfType<VideoFeedItem>())
                Shorts.Add(item.Video);
            _continuation = feed.ContinuationToken;
            CurrentIndex = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load Shorts: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(_continuation)) return;
        IsLoading = true;
        try
        {
            var feed = await _yt.GetShortsAsync(_continuation);
            foreach (var item in feed.Items.OfType<VideoFeedItem>())
                Shorts.Add(item.Video);
            _continuation = feed.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
