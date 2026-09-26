import Foundation

extension InnertubeClient {
    /// Watchtime / playback tracking URL execution (missing helper in source snapshot).
    func executeWatchtimeURLs(
        videoId: String,
        token: String,
        signatureTimestamp: Int?,
        completion: @escaping (WatchtimeURLs?) -> Void
    ) {
        // Not present in this source tree — report nothing so callers degrade gracefully.
        completion(nil)
    }
}
