using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opaline.App.Services;
using Opaline.Core.Models;
using Opaline.Core.Playback;
using Opaline.Core.Services;
using Opaline.Core.Services.Ryd;
using Opaline.Core.Services.SponsorBlock;
using Opaline.Core.Storage;

namespace Opaline.App.ViewModels;

public partial class WatchViewModel : ObservableObject
{
    private readonly IYouTubeService _yt;
    private readonly PlaybackService _playback;
    private readonly SponsorBlockService _sponsorBlock;
    private readonly ReturnYouTubeDislikeService _ryd;
    private readonly WatchHistoryStore _history;

    [ObservableProperty] private Video? video;
    [ObservableProperty] private string? playableUrl;
    [ObservableProperty] private string? audioUrl;
    [ObservableProperty] private bool isProgressive = true;
    [ObservableProperty] private bool isManifest;
    [ObservableProperty] private bool isAdaptivePair;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? qualityLabel;
    [ObservableProperty] private string? likeCount;
    [ObservableProperty] private string? dislikeCount;
    [ObservableProperty] private string? streamKindLabel;
    [ObservableProperty] private string displayTitle = string.Empty;
    [ObservableProperty] private string displayChannel = string.Empty;
    [ObservableProperty] private string displayViews = string.Empty;
    [ObservableProperty] private string displayDescription = string.Empty;
    [ObservableProperty] private bool commentsLoading;
    [ObservableProperty] private string? commentsError;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public ObservableCollection<SponsorBlockSegment> Segments { get; } = new();
    public ObservableCollection<CommentThread> Comments { get; } = new();

    public WatchViewModel(
        IYouTubeService yt,
        PlaybackService playback,
        SponsorBlockService sponsorBlock,
        ReturnYouTubeDislikeService ryd,
        WatchHistoryStore history)
    {
        _yt = yt;
        _playback = playback;
        _sponsorBlock = sponsorBlock;
        _ryd = ryd;
        _history = history;
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnVideoChanged(Video? value)
    {
        DisplayTitle = value?.Title ?? string.Empty;
        DisplayChannel = value?.ChannelTitle ?? string.Empty;
        DisplayViews = value?.FormattedViewCount ?? string.Empty;
        DisplayDescription = value?.Description ?? string.Empty;
    }

    [RelayCommand]
    public async Task LoadAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return;
        IsLoading = true;
        ErrorMessage = null;
        PlayableUrl = null;
        AudioUrl = null;
        IsManifest = false;
        IsAdaptivePair = false;
        Segments.Clear();
        Comments.Clear();
        AppLog.Info("Watch", $"Load {videoId}");

        try
        {
            var page = await _yt.GetWatchAsync(videoId);
            Video = page.Video;
            _history.AddToHistory(page.Video);
            AppLog.Info("Watch", $"meta ok title={page.Video.Title}");

            var resolved = await _playback.ResolveAsync(page);
            if (resolved is null)
            {
                ErrorMessage = "No playable stream found for this video.";
                AppLog.Warn("Watch", "no stream");
            }
            else
            {
                PlayableUrl = resolved.PrimaryUrl;
                AudioUrl = resolved.Audio?.Url;
                IsProgressive = resolved.IsProgressive;
                IsManifest = resolved.Kind is StreamKind.HlsManifest or StreamKind.DashManifest;
                IsAdaptivePair = resolved.Kind == StreamKind.AdaptivePair;
                QualityLabel = resolved.Video.DisplayLabel;
                StreamKindLabel = resolved.Kind switch
                {
                    StreamKind.HlsManifest => "HLS",
                    StreamKind.DashManifest => "DASH",
                    StreamKind.Progressive => "Progressive",
                    StreamKind.AdaptivePair => "Adaptive (A+V)",
                    StreamKind.VideoOnly => "Video only",
                    _ => resolved.Kind.ToString()
                };
                AppLog.Info("Watch", $"stream {StreamKindLabel} {QualityLabel}");
            }

            // Parallel: SB + RYD + comments
            var sbTask = _sponsorBlock.FetchSegmentsAsync(videoId);
            var rydTask = _ryd.FetchVotesAsync(videoId);
            var commentsTask = LoadCommentsInternalAsync(videoId);
            await Task.WhenAll(sbTask, rydTask, commentsTask);

            foreach (var s in await sbTask)
                Segments.Add(s);

            var votes = await rydTask;
            if (votes is not null)
            {
                LikeCount = FormatCount(votes.Likes);
                DislikeCount = FormatCount(votes.Dislikes);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load video: {ex.Message}";
            AppLog.Error("Watch", "Load failed", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCommentsInternalAsync(string videoId)
    {
        CommentsLoading = true;
        CommentsError = null;
        try
        {
            var page = await _yt.GetCommentsAsync(videoId);
            Comments.Clear();
            foreach (var c in page.Comments.Take(50))
                Comments.Add(c);
            AppLog.Info("Watch", $"comments {Comments.Count}");
        }
        catch (Exception ex)
        {
            CommentsError = ex.Message;
            AppLog.Error("Watch", "comments failed", ex);
        }
        finally
        {
            CommentsLoading = false;
        }
    }

    public SponsorBlockSegment? CheckAutoSkip(double positionSeconds)
        => _sponsorBlock.FindAutoSkip(Segments.ToList(), positionSeconds);

    [RelayCommand]
    public void AddWatchLater()
    {
        if (Video is not null)
            _history.AddWatchLater(Video);
    }

    private static string FormatCount(int n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}K",
        _ => n.ToString()
    };
}
