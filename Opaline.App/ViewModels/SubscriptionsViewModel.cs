using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class SubscriptionsViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private string? _continuation;

    public ObservableCollection<Video> Videos { get; } = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public SubscriptionsViewModel(IYouTubeService yt) => _yt = yt;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var feed = await _yt.GetSubscriptionsAsync();
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
            ErrorMessage = $"Failed to load subscriptions (sign-in required for full feed): {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
