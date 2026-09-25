using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Opaline.App.Services;
using Opaline.App.ViewModels;

namespace Opaline.App.Views;

public sealed partial class WatchPage : Page
{
    public WatchViewModel ViewModel { get; }
    private DualStreamPlayer? _player;
    private DispatcherTimer? _skipTimer;

    public WatchPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _player ??= new DualStreamPlayer();
        Player.SetMediaPlayer(_player.VideoPlayer);

        if (e.Parameter is string videoId)
            await ViewModel.LoadAsync(videoId);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _skipTimer?.Stop();
        _player?.Stop();
        Player.SetMediaPlayer(null);
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WatchViewModel.PlayableUrl)) return;
        if (string.IsNullOrEmpty(ViewModel.PlayableUrl) || _player is null) return;

        try
        {
            await _player.LoadAsync(
                ViewModel.PlayableUrl!,
                ViewModel.AudioUrl,
                ViewModel.IsManifest);
            StartSkipMonitor();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Playback error: {ex.Message}";
        }
    }

    private void StartSkipMonitor()
    {
        _skipTimer?.Stop();
        _skipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _skipTimer.Tick += (_, _) =>
        {
            if (_player is null) return;
            var pos = _player.VideoPlayer.Position.TotalSeconds;
            var seg = ViewModel.CheckAutoSkip(pos);
            if (seg is not null)
            {
                _player.Seek(TimeSpan.FromSeconds(seg.EndTime));
                SkipBar.Message = $"Skipped {seg.Category.DisplayName()} ({seg.StartTime:0}s–{seg.EndTime:0}s)";
                SkipBar.IsOpen = true;
            }
        };
        _skipTimer.Start();
    }
}
