using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.App.Services;
using Opaline.Core.Api;
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
    [ObservableProperty] private int selectedTabIndex;

    public ObservableCollection<Video> Videos { get; } = new();

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    public async Task LoadAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        ChannelId = id;
        SelectedTabIndex = 0;
        await LoadTabAsync(0);
    }

    [RelayCommand]
    public async Task LoadTabAsync(int tabIndex)
    {
        if (string.IsNullOrEmpty(ChannelId)) return;
        SelectedTabIndex = tabIndex;
        IsLoading = true;
        ErrorMessage = null;
        Videos.Clear();
        try
        {
            AppLog.Info("Channel", $"tab {tabIndex} {ChannelId}");
            ChannelPage page;
            if (tabIndex == 0)
                page = await _yt.GetChannelAsync(ChannelId);
            else
            {
                var param = tabIndex switch
                {
                    1 => InnertubeClient.ChannelTabParams.Shorts,
                    2 => InnertubeClient.ChannelTabParams.Live,
                    3 => InnertubeClient.ChannelTabParams.Playlists,
                    _ => InnertubeClient.ChannelTabParams.Videos
                };
                page = await _yt.GetChannelTabAsync(ChannelId, param);
            }

            Title = page.Channel.Title;
            AvatarUrl = page.Channel.AvatarUrl;
            BannerUrl = page.Channel.BannerUrl;
            SubscriberText = page.Channel.FormattedSubscribers;
            IsSubscribed = page.IsSubscribed;
            foreach (var v in page.Videos)
                Videos.Add(v);
            if (Videos.Count == 0)
                ErrorMessage = tabIndex == 3
                    ? "No playlists found (playlist cards may need lockup parse)."
                    : "No items on this tab.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppLog.Error("Channel", "tab load failed", ex);
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
