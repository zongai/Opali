
namespace Opaline.Core.Playback;

/// <summary>Continuous SABR pull + seek sync during playback.</summary>
public interface ISabrPlaybackController
{
    bool IsActive { get; }
    void StartPump();
    void StopPump();
    void ReportPlayerPosition(TimeSpan position);
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);
}
