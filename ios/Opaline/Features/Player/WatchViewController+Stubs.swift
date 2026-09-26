import AVFoundation
import UIKit

extension WatchViewController {
    func setupLayout() {}
    func setupNavigationBar() { title = initialVideo.title }
    func applyTheme() {
        view.backgroundColor = ThemeManager.shared.current.background
    }
    func applyResumePolicy(for video: Video) {}
    func loadInitialState() {}
    func loadWatchPage() {}
    func applyWatchPage(_ page: WatchPage) {}
    func addNotificationObservers() {}
    func updateLayoutForSize() {}
    func adjustForFloatingNavBar() {}
    func stopObservingPlayerItem(_ item: AVPlayerItem) {}
}

extension VideoPlayerView {
    func detach() {
        player?.pause()
        player = nil
    }
}
