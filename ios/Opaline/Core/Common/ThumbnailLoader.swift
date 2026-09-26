import UIKit

struct ThumbnailLoadResult {
    let image: UIImage
    let sourceURL: URL
    let pixelWidth: Int
    let pixelHeight: Int
}

enum ThumbnailLoaderError: Error {
    case unavailable
    case cancelled
    case notFound
}

final class ThumbnailLoader {
    static let shared = ThumbnailLoader()

    let memoryCache = ImageMemoryCache()
    let diskCache = ImageDiskCache()
    var transport: HTTPTransport
    let decodeQueue = DispatchQueue(
        label: "com.ytvlite.thumbnail-decode",
        qos: .utility
    )
    var prefetchTokens: [String: CancellationToken] = [:]
    let prefetchLock = NSLock()
    var missingCandidates = Set<String>()

    var cachingEnabled: Bool {
        UserDefaults.standard.object(
            forKey: UserDefaultsKeys.Cache.imageCacheEnabled
        ) as? Bool ?? true
    }

    init(transport: HTTPTransport = ServiceContainer.mediaTransport) {
        self.transport = transport
    }

    @discardableResult
    func load(
        url: URL,
        maxPixelSize: Int,
        completion: @escaping (Result<ThumbnailLoadResult, Error>) -> Void,
        videoId: String? = nil
    ) -> CancellationToken {
        let request = ThumbnailRequest(
            url: url,
            maxPixelSize: maxPixelSize,
            videoId: videoId
        )
        let token = CancellationToken()
        decodeQueue.async { [weak self] in
            self?.loadCandidate(
                request: request,
                index: 0,
                token: token,
                completion: completion
            )
        }
        return token
    }

    /// Synchronous memory-cache probe. `load` needs two queue hops even for
    /// a cached image, so a recycled cell painted its grey placeholder for a
    /// frame before the hit arrived. Callers on the main thread use this to
    /// fill the view inside the same layout pass.
    func cachedImage(
        url: URL,
        maxPixelSize: Int,
        videoId: String? = nil
    ) -> UIImage? {
        let request = ThumbnailRequest(
            url: url,
            maxPixelSize: maxPixelSize,
            videoId: videoId
        )
        for candidate in request.candidates {
            if let image = memoryCache.object(
                forKey: request.cacheKey(for: candidate)
            ) {
                return image
            }
        }
        return nil
    }

    func prefetch(
        url: URL,
        maxPixelSize: Int,
        videoId: String? = nil
    ) {
        let request = ThumbnailRequest(
            url: url,
            maxPixelSize: maxPixelSize,
            videoId: videoId
        )
        let identity = request.identity
        prefetchLock.lock()
        guard prefetchTokens[identity] == nil else {
            prefetchLock.unlock()
            return
        }
        let token = CancellationToken()
        prefetchTokens[identity] = token
        prefetchLock.unlock()
        decodeQueue.async { [weak self] in
            self?.loadCandidate(
                request: request,
                index: 0,
                token: token
            ) { [weak self] _ in
                self?.finishPrefetch(identity: identity, token: token)
            }
        }
    }

    func cancelPrefetch(
        url: URL,
        maxPixelSize: Int,
        videoId: String? = nil
    ) {
        let identity = ThumbnailRequest(
            url: url,
            maxPixelSize: maxPixelSize,
            videoId: videoId
        ).identity
        prefetchLock.lock()
        let token = prefetchTokens.removeValue(forKey: identity)
        prefetchLock.unlock()
        token?.cancel()
    }

    func clearCache() {
        memoryCache.removeAll()
        diskCache.clear()
        prefetchLock.lock()
        missingCandidates.removeAll()
        prefetchLock.unlock()
    }

    func invalidate(url: URL) {
        // Candidate stems depend on the decode target, so drop every step's.
        let candidates = ThumbnailSizing.decodeSteps.flatMap {
            ThumbnailRequest(url: url, maxPixelSize: $0).candidates
        }
        for candidate in Set(candidates) {
            for pixelSize in 1...ThumbnailSizing.maximumPixelSize {
                memoryCache.remove(
                    url: "\(candidate.absoluteString)#\(pixelSize)"
                )
            }
            diskCache.remove(url: candidate.absoluteString)
            prefetchLock.lock()
            missingCandidates.remove(candidate.absoluteString)
            prefetchLock.unlock()
        }
    }

    func isKnownMissing(_ url: URL) -> Bool {
        prefetchLock.lock()
        defer { prefetchLock.unlock() }
        return missingCandidates.contains(url.absoluteString)
    }

    func rememberMissing(_ url: URL) {
        prefetchLock.lock()
        missingCandidates.insert(url.absoluteString)
        prefetchLock.unlock()
    }

    func rememberMissingIfNeeded(
        _ result: Result<HTTPResponse, Error>,
        url: URL
    ) {
        guard let response = try? result.get(),
              response.status == 404
        else {
            return
        }
        rememberMissing(url)
    }

    func logSuccess(
        url: URL,
        image: UIImage,
        startedAt: Date
    ) {
        let elapsed = Int(Date().timeIntervalSince(startedAt) * 1_000)
        let width = image.cgImage?.width ?? 0
        let height = image.cgImage?.height ?? 0
        AppLog.img(
            "loaded \(url.lastPathComponent) "
                + "\(width)x\(height) \(elapsed)ms"
        )
    }

    func finishPrefetch(
        identity: String,
        token: CancellationToken
    ) {
        prefetchLock.lock()
        if prefetchTokens[identity] === token {
            prefetchTokens[identity] = nil
        }
        prefetchLock.unlock()
    }
}


// MARK: - Network load (recovered stub)

extension ThumbnailLoader {
    func loadCandidate(
        request: ThumbnailRequest,
        index: Int,
        token: CancellationToken,
        completion: @escaping (Result<ThumbnailLoadResult, Error>) -> Void
    ) {
        if token.isCancelled {
            completion(.failure(ThumbnailLoaderError.cancelled))
            return
        }
        guard index < request.candidates.count else {
            completion(.failure(ThumbnailLoaderError.notFound))
            return
        }
        let url = request.candidates[index]
        let key = request.cacheKey(for: url)

        if cachingEnabled, let image = memoryCache.object(forKey: key) {
            completion(.success(ThumbnailLoadResult(
                image: image,
                sourceURL: url,
                pixelWidth: image.cgImage?.width ?? 0,
                pixelHeight: image.cgImage?.height ?? 0
            )))
            return
        }

        if cachingEnabled, let fileURL = diskCache.fileURL(for: url),
           let data = try? Data(contentsOf: fileURL),
           let image = UIImage(data: data) {
            memoryCache.setObject(image, forKey: key, cost: data.count)
            completion(.success(ThumbnailLoadResult(
                image: image,
                sourceURL: url,
                pixelWidth: image.cgImage?.width ?? 0,
                pixelHeight: image.cgImage?.height ?? 0
            )))
            return
        }

        var req = URLRequest(url: url)
        req.timeoutInterval = 15
        URLSession.shared.dataTask(with: req) { [weak self] data, _, error in
            self?.decodeQueue.async {
                if token.isCancelled {
                    completion(.failure(ThumbnailLoaderError.cancelled))
                    return
                }
                guard error == nil, let data = data, let image = UIImage(data: data) else {
                    self?.loadCandidate(
                        request: request,
                        index: index + 1,
                        token: token,
                        completion: completion
                    )
                    return
                }
                if self?.cachingEnabled == true {
                    self?.memoryCache.setObject(image, forKey: key, cost: data.count)
                    self?.diskCache.store(data: data, for: url)
                }
                completion(.success(ThumbnailLoadResult(
                    image: image,
                    sourceURL: url,
                    pixelWidth: image.cgImage?.width ?? 0,
                    pixelHeight: image.cgImage?.height ?? 0
                )))
            }
        }.resume()
    }
}
