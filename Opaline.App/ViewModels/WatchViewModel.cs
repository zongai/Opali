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
    private readonly IDownloadService _downloads;

    public WatchViewModel(
        IYouTubeService yt,
        PlaybackService playback,
        SponsorBlockService sponsorBlock,
        ReturnYouTubeDislikeService ryd,
        WatchHistoryStore history,
        IDownloadService downloads)
    {
        _yt = yt;
        _playback = playback;
        _sponsorBlock = sponsorBlock;
        _ryd = ryd;
        _history = history;
        _downloads = downloads;
    }

    [ObservableProperty] private Video? video;
    [ObservableProperty] private string displayTitle = "";
    [ObservableProperty] private string displayChannel = "";
    [ObservableProperty] private string displayViews = "";
    [ObservableProperty] private string? playableUrl;
    [ObservableProperty] private string? audioUrl;
    [ObservableProperty] private bool isManifest;
    [ObservableProperty] private string? qualityLabel;
    [ObservableProperty] private string? streamKindLabel;
    [ObservableProperty] private string? likeCount;
    [ObservableProperty] private string? dislikeCount;
    [ObservableProperty] private bool isLiked;
    [ObservableProperty] private bool isDisliked;
    [ObservableProperty] private bool isSubscribed;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private bool commentsLoading;
    [ObservableProperty] private string? commentsError;
    [ObservableProperty] private string? captionText;
    [ObservableProperty] private string? selectedCaptionName;
    [ObservableProperty] private string? selectedQualityName;
    [ObservableProperty] private double downloadProgress;
    [ObservableProperty] private string? downloadStatus;
    [ObservableProperty] private bool isDownloading;

    public ObservableCollection<SponsorBlockSegment> Segments { get; } = new();
    public ObservableCollection<CommentThread> Comments { get; } = new();
    public ObservableCollection<CaptionTrack> Captions { get; } = new();
    public ObservableCollection<StreamInfo> Qualities { get; } = new();

    private WatchPage? _page;
    private ResolvedStream? _resolved;

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    public async Task LoadAsync(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return;
        IsLoading = true;
        ErrorMessage = null;
        PlayableUrl = null;
        AudioUrl = null;
        Segments.Clear();
        Comments.Clear();
        Captions.Clear();
        Qualities.Clear();
        CaptionText = null;

        try
        {
            AppLog.Info("Watch", $"load {videoId}");
            _page = await _yt.GetWatchAsync(videoId);
            Video = _page.Video;
            DisplayTitle = Video.Title;
            DisplayChannel = Video.ChannelTitle ?? "";
            DisplayViews = Video.ViewCount is long vc
                ? (vc >= 1_000_000 ? $"{vc / 1_000_000.0:0.#}M views" : vc >= 1_000 ? $"{vc / 1_000.0:0.#}K views" : $"{vc} views")
                : "";
            _history.AddHistory(Video);

            foreach (var c in _page.CaptionTracks)
                Captions.Add(c);
            foreach (var q in _page.VideoQualities)
                Qualities.Add(q);
            if (Qualities.Count > 0)
                SelectedQualityName = Qualities[0].DisplayLabel;

            _resolved = await _playback.ResolveAsync(_page);
            if (_resolved is not null)
            {
                PlayableUrl = _resolved.PrimaryUrl;
                AudioUrl = _resolved.Audio?.Url;
                IsManifest = _resolved.Kind is StreamKind.HlsManifest or StreamKind.DashManifest;
                QualityLabel = _resolved.Video.DisplayLabel;
                StreamKindLabel = _resolved.Kind switch
                {
                    StreamKind.HlsManifest => "HLS",
                    StreamKind.DashManifest => "DASH",
                    StreamKind.Progressive => "Progressive",
                    StreamKind.AdaptivePair => "Adaptive (A+V)",
                    StreamKind.VideoOnly => "Video only",
                    _ => _resolved.Kind.ToString()
                };
            }

            var sbTask = _sponsorBlock.FetchSegmentsAsync(videoId);
            var rydTask = _ryd.FetchVotesAsync(videoId);
            var commentsTask = LoadCommentsInternalAsync(videoId);
            await Task.WhenAll(sbTask, rydTask, commentsTask);

            foreach (var s in await sbTask) Segments.Add(s);
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
        finally { IsLoading = false; }
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
        }
        catch (Exception ex)
        {
            CommentsError = ex.Message;
            AppLog.Error("Watch", "comments failed", ex);
        }
        finally { CommentsLoading = false; }
    }

    public SponsorBlockSegment? CheckAutoSkip(double positionSeconds)
        => _sponsorBlock.FindAutoSkip(Segments.ToList(), positionSeconds);

    [RelayCommand]
    public void AddWatchLater()
    {
        if (Video is not null) _history.AddWatchLater(Video);
    }

    [RelayCommand]
    public async Task ToggleLikeAsync()
    {
        if (Video is null) return;
        try
        {
            if (IsLiked)
            {
                await _yt.RemoveLikeAsync(Video.Id);
                IsLiked = false;
            }
            else
            {
                await _yt.LikeAsync(Video.Id);
                IsLiked = true;
                IsDisliked = false;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Like failed (sign in required): {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ToggleDislikeAsync()
    {
        if (Video is null) return;
        try
        {
            if (IsDisliked)
            {
                await _yt.RemoveLikeAsync(Video.Id);
                IsDisliked = false;
            }
            else
            {
                await _yt.DislikeAsync(Video.Id);
                IsDisliked = true;
                IsLiked = false;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dislike failed (sign in required): {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ToggleSubscribeAsync()
    {
        if (Video?.ChannelId is null) return;
        try
        {
            if (IsSubscribed)
            {
                await _yt.UnsubscribeAsync(Video.ChannelId);
                IsSubscribed = false;
            }
            else
            {
                await _yt.SubscribeAsync(Video.ChannelId);
                IsSubscribed = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Subscribe failed (sign in required): {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SelectCaptionAsync(CaptionTrack? track)
    {
        if (track is null)
        {
            CaptionText = null;
            SelectedCaptionName = "Off";
            return;
        }
        SelectedCaptionName = track.DisplayName;
        var raw = await _yt.FetchCaptionAsync(track.BaseUrl);
        CaptionText = raw is null ? "(failed to load captions)" : TruncateCaption(raw, 4000);
    }

    [RelayCommand]
    public async Task SelectQualityAsync(StreamInfo? stream)
    {
        if (stream is null || string.IsNullOrEmpty(stream.Url)) return;
        SelectedQualityName = stream.DisplayLabel;
        // Direct URL may still need signature resolve — use raw when progressive-like
        PlayableUrl = stream.Url;
        AudioUrl = null;
        IsManifest = false;
        QualityLabel = stream.DisplayLabel;
        StreamKindLabel = stream.IsVideoOnly ? "Video only" : "Selected";
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task DownloadAsync()
    {
        if (Video is null || string.IsNullOrEmpty(PlayableUrl) || IsManifest)
        {
            DownloadStatus = "Download needs a progressive stream (try selecting a quality).";
            return;
        }
        IsDownloading = true;
        DownloadStatus = "Downloading…";
        try
        {
            var path = await _downloads.DownloadVideoAsync(
                Video, PlayableUrl!,
                new Progress<double>(p => DownloadProgress = p));
            DownloadStatus = $"Saved: {path}";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Download failed: {ex.Message}";
        }
        finally { IsDownloading = false; }
    }

    private static string TruncateCaption(string raw, int max)
    {
        // Strip simple VTT headers for display
        var lines = raw.Split('\n')
            .Where(l => !l.StartsWith("WEBVTT") && !l.Contains("-->") && !string.IsNullOrWhiteSpace(l) && !int.TryParse(l.Trim(), out _))
            .Take(80);
        var text = string.Join(" ", lines);
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static string FormatCount(int n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}K",
        _ => n.ToString()
    };
}
