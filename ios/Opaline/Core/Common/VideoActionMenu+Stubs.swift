import UIKit

// MARK: - Stubs for missing helpers (incomplete source tree)

extension VideoActionMenu {
    static func feedbackItems(
        video: Video,
        from presenter: UIViewController,
        outcome: FeedbackOutcome,
        onRemoved: (() -> Void)?
    ) -> [PlayerMenuItem] {
        // Not available in this source snapshot — return empty so menu still builds.
        []
    }

    static func presentPlaylistPicker(
        for video: Video,
        from presenter: UIViewController,
        anchor: UIView
    ) {
        // Playlist picker not present in source tree; no-op for CI build.
        AppLog.log("Stub", "presentPlaylistPicker stub: \(video.id)")
    }

    static func removeFromPlaylist(
        _ video: Video,
        playlist: (id: String, title: String),
        from presenter: UIViewController,
        onRemoved: (() -> Void)?
    ) {
        AppLog.log("Stub", "removeFromPlaylist stub: \(video.id) from \(playlist.id)")
        onRemoved?()
    }
}
