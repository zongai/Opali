using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private string? _continuation;

    public ObservableCollection<Video> Videos { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public HomeViewModel(IYouTubeService yt)
    {
        _yt = yt;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var feed = await _yt.GetHomeAsync();
            Videos.Clear();
            foreach (var item in feed.Items)
            {
                if (item is VideoFeedItem v)
                    Videos.Add(v.Video);
            }
            _continuation = feed.ContinuationToken;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home feed: {ex.Message}";
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
            var feed = await _yt.GetHomeAsync(_continuation);
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
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _continuation = null;
        await LoadAsync();
    }
}
