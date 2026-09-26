using Opaline.Core.Api;
using Opaline.Core.Models;

namespace Opaline.Core.Playback;

/// <summary>
/// iOS AutoDubSource probe chain: cheap IOS /player track list with deadline;
/// decides preferred dub before adaptive resolve commits.
/// </summary>
public sealed class AutoDubProbe
{
    /// <summary>iOS probeDeadline ~150ms; allow a bit more on Windows network.</summary>
    public static TimeSpan ProbeDeadline { get; set; } = TimeSpan.FromMilliseconds(400);

    private readonly InnertubeClient _client;

    public AutoDubProbe(InnertubeClient client) => _client = client;

    public IReadOnlyList<AudioTrackInfo> LastTracks { get; private set; } = Array.Empty<AudioTrackInfo>();
    public AudioTrackInfo? Selected { get; private set; }

    /// <summary>
    /// Race IOS listing against deadline. Returns preferred dub track id or null
    /// (play original / default).
    /// </summary>
    public async Task<AudioTrackInfo?> ProbeAsync(string videoId, CancellationToken ct = default)
    {
        Selected = null;
        LastTracks = Array.Empty<AudioTrackInfo>();
        if (!AutoDubPreference.IsEnabled)
            return null;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ProbeDeadline);
            var tracks = await _client.FetchAudioTrackListAsync(videoId, timeoutCts.Token)
                .ConfigureAwait(false);
            LastTracks = tracks;
            Selected = AutoDubPreference.AutoDubTrack(tracks);
            return Selected;
        }
        catch (OperationCanceledException)
        {
            // deadline — play without dub decision (iOS: probe too slow)
            return null;
        }
        catch
        {
            return null;
        }
    }
}
