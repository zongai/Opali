import Foundation
import AVFoundation

// MARK: - Incomplete SABR source stubs (missing +Serving / helper files)

/// Minimal route holder so SABRDelivery compiles without the full serving layer.
struct Route {
    let tracks: [SABRDelivery.Track]
    let fetcher: SABRFetcher
    let playlistPath: String
}

extension SABRDelivery {
    /// Television-style query params (cpn / alr). Stub returns the input URL unchanged.
    static func televisionParams(_ url: URL, cpn: String) -> URL {
        url
    }

    /// Signature / n-param solve throttle. Stub immediately returns the input URL.
    static func solvingThrottle(_ url: URL, completion: @escaping (URL) -> Void) {
        completion(url)
    }

    /// Builds AVPlayer-ready playback from init segments. Stub fails so other deliveries can take over.
    func buildPlayback(
        _ request: DeliveryRequest,
        fetcher: SABRFetcher,
        inits: [Int: Data],
        completion: @escaping (Result<PreparedPlayback, Error>) -> Void
    ) {
        completion(.failure(SABRError.server("SABR serving layer not present in this source snapshot")))
    }
}
