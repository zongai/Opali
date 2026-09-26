using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;
using Opaline.Core.Models;
using Opaline.Core.Services.SponsorBlock;
using Windows.System;

namespace Opaline.App.Views;

public sealed partial class WatchPage : Page
{
    public WatchViewModel ViewModel { get; }
    private DualStreamPlayer? _player;
    private DispatcherTimer? _skipTimer;
    private bool _isFullWindow;
    private bool _captionsBound;
    private OverlappedPresenterState? _savedPresenterState;

    public WatchPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        KeyDown += WatchPage_KeyDown;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _player ??= new DualStreamPlayer(DispatcherQueue);
        Player.SetMediaPlayer(_player.VideoPlayer);
        if (e.Parameter is string id)
            await ViewModel.LoadAsync(id);
        BindCaptionCombo();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_isFullWindow) ExitFullWindow();
        _skipTimer?.Stop();
        _skipTimer = null;
        _player?.Stop();
        Player.SetMediaPlayer(null);
        _player?.Dispose();
        _player = null;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void BindCaptionCombo()
    {
        if (_captionsBound) return;
        var items = new List<object> { "Off" };
        items.AddRange(ViewModel.Captions);
        CaptionCombo.ItemsSource = items;
        CaptionCombo.SelectedIndex = 0;
        _captionsBound = true;
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatchViewModel.Captions) || e.PropertyName == nameof(WatchViewModel.IsLoading))
        {
            if (!ViewModel.IsLoading && ViewModel.Captions.Count > 0)
            {
                _captionsBound = false;
                BindCaptionCombo();
            }
        }

        if (e.PropertyName == nameof(WatchViewModel.CaptionCues))
            UpdateSubtitleOverlay();

        if (e.PropertyName != nameof(WatchViewModel.PlayableUrl)) return;
        if (string.IsNullOrEmpty(ViewModel.PlayableUrl) || _player is null) return;

        try
        {
            _player.AttachSabr(ViewModel.SabrController);
            await _player.LoadAsync(ViewModel.PlayableUrl!, ViewModel.AudioUrl, ViewModel.IsManifest);
            _player.Play();
            StartSkipMonitor();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Playback error: {ex.Message}";
            CrashLog.Write("WatchPage.LoadMedia", ex);
        }
    }

    private void WatchPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _isFullWindow)
        {
            ExitFullWindow();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.F && !_isFullWindow)
        {
            EnterFullWindow();
            e.Handled = true;
        }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullWindow) ExitFullWindow();
        else EnterFullWindow();
    }

    private void EnterFullWindow()
    {
        try
        {
            var window = App.MainWindow;
            if (window is null)
            {
                // Fallback: MediaPlayerElement full window
                Player.IsFullWindow = true;
                _isFullWindow = true;
                return;
            }

            var presenter = window.AppWindow.Presenter as OverlappedPresenter;
            if (presenter is not null)
                _savedPresenterState = presenter.State;

            window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _isFullWindow = true;

            // Prefer filling the window with the player
            try { Player.IsFullWindow = true; } catch { }
            AppLog.Info("WatchPage", "entered fullscreen");
        }
        catch (Exception ex)
        {
            AppLog.Error("WatchPage", "fullscreen failed", ex);
            try
            {
                Player.IsFullWindow = true;
                _isFullWindow = true;
            }
            catch { /* ignore */ }
        }
    }

    private void ExitFullWindow()
    {
        try
        {
            try { Player.IsFullWindow = false; } catch { }
            var window = App.MainWindow;
            if (window is not null)
            {
                window.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                if (_savedPresenterState == OverlappedPresenterState.Maximized
                    && window.AppWindow.Presenter is OverlappedPresenter op)
                {
                    op.Maximize();
                }
            }
            _isFullWindow = false;
            AppLog.Info("WatchPage", "exited fullscreen");
        }
        catch (Exception ex)
        {
            CrashLog.Write("WatchPage.ExitFullWindow", ex);
            _isFullWindow = false;
        }
    }

    private void Channel_Click(object sender, RoutedEventArgs e)
    {
        var id = ViewModel.Video?.ChannelId;
        if (!string.IsNullOrEmpty(id))
            Frame.Navigate(typeof(ChannelPage), id);
    }

    private async void Quality_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: StreamInfo stream })
            await ViewModel.SelectQualityAsync(stream);
    }

    private async void Caption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox box) return;
        if (box.SelectedItem is CaptionTrack track)
            await ViewModel.SelectCaptionAsync(track);
        else
            await ViewModel.SelectCaptionAsync(null);
        UpdateSubtitleOverlay();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem item } && item.Tag is string tag)
            ViewModel.TargetLanguage = tag;
    }

    private void StartSkipMonitor()
    {
        _skipTimer?.Stop();
        _skipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _skipTimer.Tick += (_, _) =>
        {
            if (_player is null) return;
            try
            {
                var pos = _player.VideoPlayer.Position.TotalSeconds;
                var seg = ViewModel.CheckAutoSkip(pos);
                if (seg is not null)
                {
                    _player.Seek(TimeSpan.FromSeconds(seg.EndTime));
                    SkipBar.Message = $"Skipped {seg.Category.DisplayName()} ({seg.StartTime:0}s–{seg.EndTime:0}s)";
                    SkipBar.IsOpen = true;
                }
                UpdateSubtitleOverlay(pos);
            }
            catch (Exception ex) { CrashLog.Write("WatchPage.SkipMonitor", ex); }
        };
        _skipTimer.Start();
    }

    private void UpdateSubtitleOverlay(double? positionSeconds = null)
    {
        try
        {
            var cues = ViewModel.CaptionCues;
            if (cues is null || cues.Count == 0)
            {
                SubtitleOverlay.Text = "";
                return;
            }
            var pos = positionSeconds
                ?? _player?.VideoPlayer.Position.TotalSeconds
                ?? 0;
            var t = TimeSpan.FromSeconds(pos);
            var cue = cues.FirstOrDefault(c => t >= c.Start && t <= c.End);
            SubtitleOverlay.Text = cue?.Text ?? "";
        }
        catch
        {
            /* ignore overlay errors */
        }
    }

    private async void PlaylistPicker_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaylistAddOption opt)
            await ViewModel.AddToPlaylistAsync(opt);
    }

    private async void Related_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Video v && !string.IsNullOrEmpty(v.Id))
            await ViewModel.LoadAsync(v.Id);
    }
}
