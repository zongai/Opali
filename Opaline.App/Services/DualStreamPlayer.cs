using System;
using System.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Opaline.App.Services;

/// <summary>
/// Playback controller that supports:
/// <list type="bullet">
///   <item>Single URL (progressive / HLS / DASH via AdaptiveMediaSource)</item>
///   <item>Dual adaptive streams: separate video + audio MediaPlayers kept in sync</item>
/// </list>
/// Attach the video <see cref="MediaPlayer"/> to a <c>MediaPlayerElement</c>.
/// </summary>
public sealed class DualStreamPlayer : IDisposable
{
    public MediaPlayer VideoPlayer { get; } = new() { AutoPlay = true };
    public MediaPlayer AudioPlayer { get; } = new() { AutoPlay = true };

    private bool _dual;
    private bool _syncing;
    private DispatcherTimerHook? _syncHook;

    public bool IsDual => _dual;

    /// <summary>Wire up after MediaPlayerElement.SetMediaPlayer(VideoPlayer).</summary>
    public DualStreamPlayer()
    {
        VideoPlayer.MediaFailed += (_, e) =>
            System.Diagnostics.Debug.WriteLine($"Video failed: {e.ErrorMessage}");
        AudioPlayer.MediaFailed += (_, e) =>
            System.Diagnostics.Debug.WriteLine($"Audio failed: {e.ErrorMessage}");
    }

    public async Task LoadAsync(string primaryUrl, string? audioUrl, bool isManifest)
    {
        Stop();
        _dual = !string.IsNullOrEmpty(audioUrl) && !isManifest;

        if (isManifest)
        {
            // HLS / DASH — AdaptiveMediaSource handles A+V
            try
            {
                var result = await AdaptiveMediaSource.CreateFromUriAsync(new Uri(primaryUrl));
                if (result.Status == AdaptiveMediaSourceCreationStatus.Success)
                {
                    VideoPlayer.Source = MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
                    AudioPlayer.Source = null;
                    return;
                }
            }
            catch
            {
                // fall through to plain MediaSource
            }
            VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(primaryUrl));
            AudioPlayer.Source = null;
            return;
        }

        VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(primaryUrl));

        if (_dual)
        {
            AudioPlayer.Source = MediaSource.CreateFromUri(new Uri(audioUrl!));
            // Mute video track if it somehow has audio; keep AudioPlayer as the only sound
            VideoPlayer.IsMuted = true;
            AudioPlayer.IsMuted = false;
            StartSyncLoop();
        }
        else
        {
            VideoPlayer.IsMuted = false;
            AudioPlayer.Source = null;
        }
    }

    public void Play()
    {
        VideoPlayer.Play();
        if (_dual) AudioPlayer.Play();
    }

    public void Pause()
    {
        VideoPlayer.Pause();
        if (_dual) AudioPlayer.Pause();
    }

    public void Seek(TimeSpan position)
    {
        _syncing = true;
        try
        {
            VideoPlayer.Position = position;
            if (_dual) AudioPlayer.Position = position;
        }
        finally
        {
            _syncing = false;
        }
    }

    public void Stop()
    {
        _syncHook?.Stop();
        _syncHook = null;
        VideoPlayer.Pause();
        AudioPlayer.Pause();
        VideoPlayer.Source = null;
        AudioPlayer.Source = null;
        _dual = false;
    }

    private void StartSyncLoop()
    {
        // Re-sync audio to video every 500ms if drift exceeds 80ms
        _syncHook?.Stop();
        _syncHook = new DispatcherTimerHook(TimeSpan.FromMilliseconds(500), () =>
        {
            if (!_dual || _syncing) return;
            try
            {
                var drift = (AudioPlayer.Position - VideoPlayer.Position).TotalMilliseconds;
                if (Math.Abs(drift) > 80)
                    AudioPlayer.Position = VideoPlayer.Position;

                // Mirror play/pause state
                if (VideoPlayer.CurrentState == MediaPlayerState.Playing
                    && AudioPlayer.CurrentState != MediaPlayerState.Playing)
                    AudioPlayer.Play();
                else if (VideoPlayer.CurrentState == MediaPlayerState.Paused
                         && AudioPlayer.CurrentState == MediaPlayerState.Playing)
                    AudioPlayer.Pause();
            }
            catch
            {
                // players may be disposed mid-tick
            }
        });
        _syncHook.Start();
    }

    public void Dispose()
    {
        Stop();
        VideoPlayer.Dispose();
        AudioPlayer.Dispose();
    }
}

/// <summary>
/// Tiny timer abstraction so DualStreamPlayer can be unit-tested without UI thread.
/// On UI, callers can replace with DispatcherTimer via optional hook;
/// default uses System.Threading.Timer.
/// </summary>
internal sealed class DispatcherTimerHook
{
    private readonly Timer _timer;
    private readonly Action _tick;

    public DispatcherTimerHook(TimeSpan interval, Action tick)
    {
        _tick = tick;
        _timer = new Timer(_ => _tick(), null, Timeout.Infinite, Timeout.Infinite);
        Interval = interval;
    }

    public TimeSpan Interval { get; }

    public void Start() => _timer.Change(Interval, Interval);
    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);
}
