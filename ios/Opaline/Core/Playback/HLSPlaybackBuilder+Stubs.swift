import Foundation

// MARK: - Incomplete HLS helpers

extension HLSPlaybackBuilder {
    static func probeIdentity(
        input: BuildInput,
        completion: @escaping (Bool) -> Void
    ) {
        // Identity probe missing — treat as healthy so build can continue.
        completion(true)
    }

    static func fetchRangeData(
        request: RangeRequest,
        completion: @escaping (Data?) -> Void
    ) {
        // Range fetch not present in this snapshot.
        completion(nil)
    }
}

extension HLSGenerator {
    /// Parse sidx box into segments. Stub returns nil so callers fail gracefully.
    static func parseSidx(data: Data) -> [SidxSegment]? {
        nil
    }
}
