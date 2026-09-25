using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Opaline.App.Services;

/// <summary>
/// Playback controller: progressive / HLS / DASH, or dual adaptive A+V with sync.
/// </summary>
public sealed class DualStreamPlayer : IDisposable
{
    public MediaPlayer VideoPlayer { get; } = new() { AutoPlay = true };
    public MediaPlayer AudioPlayer { get; } = new() { AutoPlay = true };

    private bool _dual;
    private bool _syncing;
    private DispatcherQueueTimer? _syncTimer;
    private readonly DispatcherQueue? _dispatcher;

    public bool IsDual => _dual;

    public DualStreamPlayer(DispatcherQueue? dispatcher = null)
    {
        _dispatcher = dispatcher ?? DispatcherQueue.GetForCurrentThread();
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
                // fall through
            }
            VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(primaryUrl));
            AudioPlayer.Source = null;
            return;
        }

        VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(primaryUrl));

        if (_dual)
        {
            AudioPlayer.Source = MediaSource.CreateFromUri(new Uri(audioUrl!));
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
        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTick;
            _syncTimer = null;
        }
        VideoPlayer.Pause();
        AudioPlayer.Pause();
        VideoPlayer.Source = null;
        AudioPlayer.Source = null;
        _dual = false;
    }

    private void StartSyncLoop()
    {
        if (_dispatcher is null) return;

        if (_syncTimer is not null)
        {
            _syncTimer.Stop();
            _syncTimer.Tick -= OnSyncTick;
        }

        _syncTimer = _dispatcher.CreateTimer();
        _syncTimer.Interval = TimeSpan.FromMilliseconds(500);
        _syncTimer.IsRepeating = true;
        _syncTimer.Tick += OnSyncTick;
        _syncTimer.Start();
    }

    private void OnSyncTick(DispatcherQueueTimer sender, object args)
    {
        if (!_dual || _syncing) return;
        try
        {
            var drift = (AudioPlayer.Position - VideoPlayer.Position).TotalMilliseconds;
            if (Math.Abs(drift) > 80)
                AudioPlayer.Position = VideoPlayer.Position;

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
    }

    public void Dispose()
    {
        Stop();
        VideoPlayer.Dispose();
        AudioPlayer.Dispose();
    }
}
