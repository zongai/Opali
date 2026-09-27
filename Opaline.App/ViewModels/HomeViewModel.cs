using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.App.Services;
using Opaline.Core.Models;
using Opaline.Core.Net;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private string? _continuation;

    public ObservableCollection<Video> Videos { get; } = new();

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? errorMessage;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public HomeViewModel(IYouTubeService yt)
    {
        _yt = yt;
        Opaline.App.Controls.VideoCard.VideoHiddenByFeedback += id =>
        {
            var hit = Videos.FirstOrDefault(v => v.Id == id);
            if (hit is not null) Videos.Remove(hit);
        };
    }


    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var feed = await LoadFeedWithRetryAsync(null).ConfigureAwait(true);
            Videos.Clear();
            foreach (var item in feed.Items)
            {
                if (item is not VideoFeedItem v) continue;
                if (v.Video.IsShort) continue;
                if (v.Video.Duration is { } d && d.TotalSeconds > 0 && d.TotalSeconds <= 60) continue;
                Videos.Add(v.Video);
            }
            AppLog.Info("Home", $"loaded {Videos.Count} videos");
            _continuation = feed.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home feed: {ex.Message}";
            AppLog.Error("Home", "Load failed", ex);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoading || string.IsNullOrEmpty(_continuation)) return;
        IsLoading = true;
        try
        {
            var feed = await LoadFeedWithRetryAsync(_continuation).ConfigureAwait(true);
            foreach (var item in feed.Items)
            {
                if (item is VideoFeedItem v)
                    Videos.Add(v.Video);
            }
            _continuation = feed.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load more: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _continuation = null;
        await LoadAsync();
    }

    private async Task<HomeFeed> LoadFeedWithRetryAsync(string? continuation)
    {
        try
        {
            return await _yt.GetHomeAsync(continuation).ConfigureAwait(false);
        }
        catch (Exception ex) when (AppHttp.IsTransientSsl(ex))
        {
            await Task.Delay(600).ConfigureAwait(false);
            return await _yt.GetHomeAsync(continuation).ConfigureAwait(false);
        }
    }
}
