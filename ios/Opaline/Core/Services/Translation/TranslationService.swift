import Foundation

/// Harbor-style translation chain: Google (free gtx) → MyMemory → Lingva.
/// Optional DeepL via `OPALINE_DEEPL_KEY` / `DEEPL_API_KEY` environment.
enum TranslationEngine: String, CaseIterable {
    case google, myMemory, lingva, deepL
}

enum TranslationError: LocalizedError {
    case empty
    case rateLimited(TranslationEngine)
    case failed(String)

    var errorDescription: String? {
        switch self {
        case .empty: return "Empty text"
        case .rateLimited(let e): return "Rate limited: \(e.rawValue)"
        case .failed(let m): return m
        }
    }
}

final class TranslationService {
    static let shared = TranslationService()

    private let session: URLSession
    private let cache = NSCache<NSString, NSString>()
    private var limitedUntil: [TranslationEngine: Date] = [:]
    private let lock = NSLock()

    init(session: URLSession = .shared) {
        self.session = session
    }

    /// Target BCP-47-ish code used by free engines (e.g. zh-CN, en, ja).
    static var preferredTarget: String {
        let code = AppLanguage.effective.rawValue
        switch code {
        case "zh-Hans", "zh-CN", "zh": return "zh-CN"
        case "zh-Hant", "zh-TW": return "zh-TW"
        default: return code
        }
    }

    func translate(
        _ text: String,
        target: String = preferredTarget,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            completion(.failure(TranslationError.empty))
            return
        }
        let key = "\(target)|\(trimmed)" as NSString
        if let hit = cache.object(forKey: key) {
            completion(.success(hit as String))
            return
        }
        let engines: [TranslationEngine] = [.google, .myMemory, .lingva, .deepL]
        tryEngines(engines, text: trimmed, target: target, key: key, completion: completion)
    }

    func translateMany(
        _ texts: [String],
        target: String = preferredTarget,
        completion: @escaping (Result<[String], Error>) -> Void
    ) {
        guard !texts.isEmpty else {
            completion(.success([]))
            return
        }
        var results = Array(repeating: "", count: texts.count)
        let group = DispatchGroup()
        var firstError: Error?
        let lock = NSLock()
        for (i, t) in texts.enumerated() {
            group.enter()
            translate(t, target: target) { result in
                switch result {
                case .success(let s):
                    results[i] = s
                case .failure(let e):
                    lock.lock()
                    if firstError == nil { firstError = e }
                    lock.unlock()
                    results[i] = t
                }
                group.leave()
            }
        }
        group.notify(queue: .main) {
            if results.allSatisfy({ $0.isEmpty }), let firstError {
                completion(.failure(firstError))
            } else {
                completion(.success(results))
            }
        }
    }

    /// Strip VTT/XML timing, translate text lines, return plain translated body.
    func translateCaptionBody(
        _ raw: String,
        target: String = preferredTarget,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let lines = raw
            .components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { line in
                guard !line.isEmpty else { return false }
                if line.hasPrefix("WEBVTT") { return false }
                if line.contains("-->") { return false }
                if line.hasPrefix("NOTE") { return false }
                if line.allSatisfy(\.isNumber) { return false }
                return true
            }
        guard !lines.isEmpty else {
            completion(.success(""))
            return
        }
        var chunks: [String] = []
        var buf = ""
        for line in lines {
            if buf.count + line.count > 800 {
                chunks.append(buf)
                buf = ""
            }
            if !buf.isEmpty { buf += " " }
            buf += line
        }
        if !buf.isEmpty { chunks.append(buf) }
        translateMany(chunks, target: target) { result in
            switch result {
            case .success(let parts):
                completion(.success(parts.joined(separator: "\n")))
            case .failure(let e):
                completion(.failure(e))
            }
        }
    }

    // MARK: - Chain

    private func tryEngines(
        _ engines: [TranslationEngine],
        text: String,
        target: String,
        key: NSString,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        guard let engine = engines.first else {
            completion(.failure(TranslationError.failed("All engines failed")))
            return
        }
        let rest = Array(engines.dropFirst())
        lock.lock()
        if let until = limitedUntil[engine], until > Date() {
            lock.unlock()
            tryEngines(rest, text: text, target: target, key: key, completion: completion)
            return
        }
        lock.unlock()

        translate(engine: engine, text: text, target: target) { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let s) where !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty:
                self.cache.setObject(s as NSString, forKey: key)
                completion(.success(s))
            case .success:
                self.tryEngines(rest, text: text, target: target, key: key, completion: completion)
            case .failure(let e):
                if case TranslationError.rateLimited = e {
                    self.lock.lock()
                    self.limitedUntil[engine] = Date().addingTimeInterval(60)
                    self.lock.unlock()
                }
                self.tryEngines(rest, text: text, target: target, key: key, completion: completion)
            }
        }
    }

    private func translate(
        engine: TranslationEngine,
        text: String,
        target: String,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        switch engine {
        case .google: google(text, target, completion)
        case .myMemory: myMemory(text, target, completion)
        case .lingva: lingva(text, target, completion)
        case .deepL: deepL(text, target, completion)
        }
    }

    // MARK: - Engines

    private func google(
        _ text: String,
        _ target: String,
        _ completion: @escaping (Result<String, Error>) -> Void
    ) {
        var comps = URLComponents(string: "https://translate.googleapis.com/translate_a/single")!
        comps.queryItems = [
            URLQueryItem(name: "client", value: "gtx"),
            URLQueryItem(name: "sl", value: "auto"),
            URLQueryItem(name: "tl", value: target),
            URLQueryItem(name: "dt", value: "t"),
            URLQueryItem(name: "q", value: text)
        ]
        guard let url = comps.url else {
            completion(.failure(TranslationError.failed("Bad Google URL")))
            return
        }
        var req = URLRequest(url: url)
        req.setValue("Mozilla/5.0", forHTTPHeaderField: "User-Agent")
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 429 {
                completion(.failure(TranslationError.rateLimited(.google)))
                return
            }
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [Any],
                  let sentences = json.first as? [Any] else {
                completion(.failure(TranslationError.failed("Google parse")))
                return
            }
            var out = ""
            for item in sentences {
                if let row = item as? [Any], let s = row.first as? String {
                    out += s
                }
            }
            completion(.success(out))
        }.resume()
    }

    /// MyMemory expects RFC3066 / ISO codes with hyphen (e.g. `zh-CN`), not `ZHCN`.
    private static func normalizeMyMemory(_ code: String) -> String {
        switch code.lowercased() {
        case "zh", "zh-hans", "zh-cn": return "zh-CN"
        case "zh-hant", "zh-tw": return "zh-TW"
        default: return code
        }
    }

    private func myMemory(
        _ text: String,
        _ target: String,
        _ completion: @escaping (Result<String, Error>) -> Void
    ) {
        let tl = Self.normalizeMyMemory(target)
        var comps = URLComponents(string: "https://api.mymemory.translated.net/get")!
        comps.queryItems = [
            URLQueryItem(name: "q", value: String(text.prefix(500))),
            URLQueryItem(name: "langpair", value: "autodetect|\(tl)")
        ]
        guard let url = comps.url else {
            completion(.failure(TranslationError.failed("Bad MyMemory URL")))
            return
        }
        session.dataTask(with: url) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            if (response as? HTTPURLResponse)?.statusCode == 429 {
                completion(.failure(TranslationError.rateLimited(.myMemory)))
                return
            }
            guard let data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let rd = obj["responseData"] as? [String: Any],
                  let translated = rd["translatedText"] as? String else {
                completion(.failure(TranslationError.failed("MyMemory parse")))
                return
            }
            // MyMemory often returns the error string as translatedText when lang is invalid
            let status = obj["responseStatus"] as? Int ?? 0
            let upper = translated.uppercased()
            if status != 200
                || upper.contains("INVALID TARGET LANGUAGE")
                || upper.contains("MYMEMORY WARNING") {
                completion(.failure(TranslationError.failed(translated)))
                return
            }
            completion(.success(translated))
        }.resume()
    }

    private func lingva(
        _ text: String,
        _ target: String,
        _ completion: @escaping (Result<String, Error>) -> Void
    ) {
        let tl = target.replacingOccurrences(of: "-", with: "_").lowercased()
        let encoded = text.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? text
        guard let url = URL(string: "https://lingva.ml/api/v1/auto/\(tl)/\(encoded)") else {
            completion(.failure(TranslationError.failed("Bad Lingva URL")))
            return
        }
        session.dataTask(with: url) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            if (response as? HTTPURLResponse)?.statusCode == 429 {
                completion(.failure(TranslationError.rateLimited(.lingva)))
                return
            }
            guard let data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let translated = obj["translation"] as? String else {
                completion(.failure(TranslationError.failed("Lingva parse")))
                return
            }
            completion(.success(translated))
        }.resume()
    }

    private func deepL(
        _ text: String,
        _ target: String,
        _ completion: @escaping (Result<String, Error>) -> Void
    ) {
        let key = ProcessInfo.processInfo.environment["OPALINE_DEEPL_KEY"]
            ?? ProcessInfo.processInfo.environment["DEEPL_API_KEY"]
        guard let key, !key.isEmpty else {
            completion(.failure(TranslationError.failed("No DeepL key")))
            return
        }
        let free = key.hasSuffix(":fx")
        let base = free
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate"
        guard let url = URL(string: base) else {
            completion(.failure(TranslationError.failed("Bad DeepL URL")))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("DeepL-Auth-Key \(key)", forHTTPHeaderField: "Authorization")
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        let tl = target.uppercased().replacingOccurrences(of: "ZH-CN", with: "ZH")
            .replacingOccurrences(of: "ZH-TW", with: "ZH")
        let body = "text=\(text.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? text)&target_lang=\(tl)"
        req.httpBody = body.data(using: .utf8)
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            if (response as? HTTPURLResponse)?.statusCode == 429 {
                completion(.failure(TranslationError.rateLimited(.deepL)))
                return
            }
            guard let data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let translations = obj["translations"] as? [[String: Any]],
                  let first = translations.first,
                  let translated = first["text"] as? String else {
                completion(.failure(TranslationError.failed("DeepL parse")))
                return
            }
            completion(.success(translated))
        }.resume()
    }
}
