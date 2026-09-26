using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.App.Services;
using Opaline.Core.Models;
using Opaline.Core.Services;

namespace Opaline.App.ViewModels;

public partial class ChannelViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;

    public ChannelViewModel(IYouTubeService yt) => _yt = yt;

    [ObservableProperty] private string title = "Channel";
    [ObservableProperty] private string? avatarUrl;
    [ObservableProperty] private string? bannerUrl;
    [ObservableProperty] private string? subscriberText;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private bool isSubscribed;
    [ObservableProperty] private string? channelId;

    public ObservableCollection<Video> Videos { get; } = new();

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    public async Task LoadAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        ChannelId = id;
        IsLoading = true;
        ErrorMessage = null;
        Videos.Clear();
        try
        {
            AppLog.Info("Channel", $"load {id}");
            var page = await _yt.GetChannelAsync(id);
            Title = page.Channel.Title;
            AvatarUrl = page.Channel.AvatarUrl;
            BannerUrl = page.Channel.BannerUrl;
            SubscriberText = page.Channel.FormattedSubscribers;
            IsSubscribed = page.IsSubscribed;
            foreach (var v in page.Videos)
                Videos.Add(v);
            if (Videos.Count == 0)
                ErrorMessage = "No videos found for this channel.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("Channel", "load failed", ex);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task ToggleSubscribeAsync()
    {
        if (string.IsNullOrEmpty(ChannelId)) return;
        try
        {
            if (IsSubscribed)
            {
                await _yt.UnsubscribeAsync(ChannelId);
                IsSubscribed = false;
            }
            else
            {
                await _yt.SubscribeAsync(ChannelId);
                IsSubscribed = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Subscribe failed: {ex.Message}";
        }
    }
}
