import Foundation

extension InnertubeClient: ShortsService {
    func fetchShortsSequence(
        seed: String,
        completion: @escaping (Result<ShortsSequencePage, Error>) -> Void
    ) {
        completion(.failure(NSError(domain: "Shorts", code: -1, userInfo: [NSLocalizedDescriptionKey: "Shorts sequence not in source snapshot"])))
    }
}

struct ShortsSequencePage {
    let videos: [Video]
    let continuation: String?
}
