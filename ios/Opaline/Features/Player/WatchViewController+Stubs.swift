import AVFoundation
import UIKit

// MARK: - Missing types

enum PlayerSheetKind {
    case comments
    case description
    case quality
    case captions
    case settings
}

struct FullscreenSnapshot {
    var bounds: CGRect = .zero
    var transform: CGAffineTransform = .identity
}

// MARK: - WatchViewController stubs

extension WatchViewController {
    func setupLayout() {}
    func setupNavigationBar() { title = initialVideo.title }
    func applyTheme() {
        view.backgroundColor = ThemeManager.shared.background
    }
    func applyResumePolicy(for video: Video) {}
    func loadInitialState() {}
    func loadWatchPage() {}
    func applyWatchPage(_ page: WatchPage) {}
    func addNotificationObservers() {}
    func updateLayoutForSize() {}
    func adjustForFloatingNavBar() {}
    func stopObservingPlayerItem(_ item: AVPlayerItem) {}
    func exitFullscreenIfNeeded() {}
    func keepOpeningOrientation() {}

    // PlaybackContext
    func attachPrepared(_ prepared: PreparedPlayback, resumeAt: CMTime?) {}
    func updateStatusLabel(_ text: String) {}
    func showPlaybackError(_ message: String) {}
    func startObservingPlayerItem(_ item: AVPlayerItem) {}
    func setCaptionTracks(_ tracks: [SubtitleTrack]) {}
}

extension WatchViewController: PlaybackContext {}

extension VideoPlayerView {
    func detach() {
        player?.pause()
        player = nil
    }
}

func refreshSupportedOrientations() {}
