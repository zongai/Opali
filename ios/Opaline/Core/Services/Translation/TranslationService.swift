import Foundation
import NaturalLanguage

/// Harbor-style translation chain: Google (free gtx) → MyMemory → Lingva.
/// Optional DeepL via `OPALINE_DEEPL_KEY` / `DEEPL_API_KEY` environment.
enum TranslationEngine: String, CaseIterable {
    case google, myMemory, lingva, deepL
}

enum TranslationError: LocalizedError {
    case empty
    case rateLimited(TranslationEngine)
    case noDeepLKey
    case failed(String)
    /// All engines in the chain failed; `details` is engine → reason.
    case allFailed(target: String, details: [(TranslationEngine, String)])

    var errorDescription: String? {
        switch self {
        case .empty:
            return "player.translate.error.empty".localized
        case .rateLimited(let e):
            return "player.translate.error.rateLimited".localized(with: e.rawValue)
        case .noDeepLKey:
            return "player.translate.error.noDeepLKey".localized
        case .failed(let m):
            return m
        case .allFailed(let target, let details):
            let lines = details.map { "\($0.0.rawValue): \($0.1)" }.joined(separator: "\n")
            let header = "player.translate.error.allFailed".localized(with: target)
            return header + "\n" + lines
" + lines
        }
    }
}


/// Canonical BCP-47-ish codes and per-engine mappings for accurate target
/// language + source auto-detect.
enum TranslationLanguageNorm {
    /// App / settings → stable form used in cache keys and settings.
    static func canonical(_ code: String) -> String {
        switch code.lowercased() {
        case "zh", "zh-hans", "zh-cn", "zh_hans", "zh_cn": return "zh-CN"
        case "zh-hant", "zh-tw", "zh_hant", "zh_tw": return "zh-TW"
        case "pt-br", "pt_br": return "pt"
        case "nb", "nn": return "no"
        default:
            let lower = code.lowercased().replacingOccurrences(of: "_", with: "-")
            // Keep primary subtag only (en-US → en), except zh already handled.
            if let primary = lower.split(separator: "-").first {
                return String(primary)
            }
            return lower
        }
    }

    static func forGoogle(_ code: String) -> String {
        switch canonical(code) {
        case "zh-CN": return "zh-CN"
        case "zh-TW": return "zh-TW"
        default: return canonical(code).lowercased()
        }
    }

    static func forMyMemory(_ code: String) -> String {
        switch canonical(code) {
        case "zh-CN": return "zh-CN"
        case "zh-TW": return "zh-TW"
        default: return canonical(code)
        }
    }

    /// Lingva uses underscore form (zh_CN).
    static func forLingva(_ code: String) -> String {
        switch canonical(code) {
        case "zh-CN": return "zh_CN"
        case "zh-TW": return "zh_TW"
        default: return canonical(code).replacingOccurrences(of: "-", with: "_").lowercased()
        }
    }

    /// DeepL uppercase; ZH vs ZH-HANT for simplified/traditional.
    static func forDeepL(_ code: String) -> String {
        switch canonical(code) {
        case "zh-CN": return "ZH"
        case "zh-TW": return "ZH-HANT"
        case "en": return "EN"
        case "ja": return "JA"
        case "ko": return "KO"
        case "es": return "ES"
        case "fr": return "FR"
        case "de": return "DE"
        case "ru": return "RU"
        case "pt": return "PT-PT"
        case "it": return "IT"
        case "tr": return "TR"
        case "uk": return "UK"
        case "vi": return "VI"
        case "id": return "ID"
        case "ar": return "AR"
        default: return canonical(code).uppercased()
        }
    }

    /// Whether two codes refer to the same written language (for skip-translate).
    static func isSameLanguage(_ a: String, _ b: String) -> Bool {
        let ca = canonical(a)
        let cb = canonical(b)
        if ca == cb { return true }
        let baseA = ca.split(separator: "-").first.map(String.init) ?? ca
        let baseB = cb.split(separator: "-").first.map(String.init) ?? cb
        if baseA == "zh" || baseB == "zh" {
            return ca == cb
        }
        return baseA.lowercased() == baseB.lowercased()
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

    /// Target from settings (or app language when not overridden).
    static var preferredTarget: String {
        TranslationPreferences.effectiveTargetLanguage
    }

    /// App language only — used when settings follow system.
    static var preferredTargetFromAppLanguage: String {
        TranslationLanguageNorm.canonical(AppLanguage.effective.rawValue)
    }

    /// NLLanguageRecognizer + script heuristics for auto source language.
    static func detectSourceLanguage(of text: String) -> String? {
        let sample = String(text.prefix(400))
        let recognizer = NLLanguageRecognizer()
        recognizer.processString(sample)
        if let lang = recognizer.dominantLanguage {
            return TranslationLanguageNorm.canonical(lang.rawValue)
        }
        let han = sample.unicodeScalars.filter { (0x4E00...0x9FFF).contains($0.value) }.count
        if han >= max(3, sample.count / 4) {
            let pref = preferredTarget
            if TranslationLanguageNorm.canonical(pref).hasPrefix("zh") {
                return TranslationLanguageNorm.canonical(pref)
            }
            return "zh-CN"
        }
        return nil
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
        let normalizedTarget = TranslationLanguageNorm.canonical(target)
        // Client-side detect: skip when already in the target language.
        if let detected = Self.detectSourceLanguage(of: trimmed),
           TranslationLanguageNorm.isSameLanguage(detected, normalizedTarget) {
            completion(.success(trimmed))
            return
        }
        let key = "\(normalizedTarget)|\(trimmed)" as NSString
        if let hit = cache.object(forKey: key) {
            completion(.success(hit as String))
            return
        }
        let engines = TranslationPreferences.engineChain
        tryEngines(engines, text: trimmed, target: normalizedTarget, key: key, completion: completion)
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
        completion: @escaping (Result<String, Error>) -> Void,
        failures: [(TranslationEngine, String)] = []
    ) {
        guard let engine = engines.first else {
            let details = failures.isEmpty
                ? [(.google, "player.translate.error.unknown".localized)]
                : failures
            completion(.failure(TranslationError.allFailed(target: target, details: details)))
            return
        }
        let rest = Array(engines.dropFirst())
        lock.lock()
        if let until = limitedUntil[engine], until > Date() {
            lock.unlock()
            var next = failures
            next.append((engine, "player.translate.error.rateLimitedShort".localized))
            tryEngines(rest, text: text, target: target, key: key, completion: completion, failures: next)
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
                var next = failures
                next.append((engine, "player.translate.error.emptyResult".localized))
                self.tryEngines(rest, text: text, target: target, key: key, completion: completion, failures: next)
            case .failure(let e):
                if case TranslationError.rateLimited = e {
                    self.lock.lock()
                    self.limitedUntil[engine] = Date().addingTimeInterval(60)
                    self.lock.unlock()
                }
                var next = failures
                let reason = (e as? LocalizedError)?.errorDescription ?? e.localizedDescription
                next.append((engine, reason))
                self.tryEngines(rest, text: text, target: target, key: key, completion: completion, failures: next)
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
        let tl = TranslationLanguageNorm.forGoogle(target)
        comps.queryItems = [
            URLQueryItem(name: "client", value: "gtx"),
            URLQueryItem(name: "sl", value: "auto"),
            URLQueryItem(name: "tl", value: tl),
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
                completion(.failure(TranslationError.failed("player.translate.error.googleParse".localized)))
                return
            }
            var out = ""
            for item in sentences {
                if let row = item as? [Any], let s = row.first as? String {
                    out += s
                }
            }
            // Response index 2 is the auto-detected source language.
            if json.count > 2, let detected = json[2] as? String,
               TranslationLanguageNorm.isSameLanguage(detected, target) {
                completion(.success(text))
                return
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
        let tl = TranslationLanguageNorm.forMyMemory(target)
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
                completion(.failure(TranslationError.failed("player.translate.error.myMemoryParse".localized)))
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
        let tl = TranslationLanguageNorm.forLingva(target)
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
                completion(.failure(TranslationError.failed("player.translate.error.lingvaParse".localized)))
                return
            }
            completion(.success(translated))
        }.resume()
    }

    // MARK: - DeepL key test

    /// Probe each key with a tiny translate request. Reports per-key OK / error.
    func testDeepLKeys(
        _ keys: [String]? = nil,
        completion: @escaping ([(mask: String, ok: Bool, detail: String)]) -> Void
    ) {
        let list = keys ?? TranslationPreferences.deepLAPIKeys
        guard !list.isEmpty else {
            completion([])
            return
        }
        var results = Array(repeating: (mask: "", ok: false, detail: ""), count: list.count)
        let group = DispatchGroup()
        let lock = NSLock()
        for (i, key) in list.enumerated() {
            group.enter()
            deepLRequest(text: "OK", target: "EN", apiKey: key) { result in
                let mask = Self.maskKey(key)
                let entry: (mask: String, ok: Bool, detail: String)
                switch result {
                case .success:
                    entry = (mask, true, "settings.translation.deepL.testOK".localized)
                case .failure(let e):
                    let detail = (e as? LocalizedError)?.errorDescription ?? e.localizedDescription
                    entry = (mask, false, detail)
                }
                lock.lock()
                results[i] = entry
                lock.unlock()
                group.leave()
            }
        }
        group.notify(queue: .main) {
            completion(results)
        }
    }

    private static func maskKey(_ key: String) -> String {
        if key.count <= 8 { return "••••" }
        return String(key.prefix(4)) + "••••" + String(key.suffix(4))
    }

    private func deepL(
        _ text: String,
        _ target: String,
        _ completion: @escaping (Result<String, Error>) -> Void
    ) {
        let keys = TranslationPreferences.deepLAPIKeys
        guard !keys.isEmpty else {
            completion(.failure(TranslationError.noDeepLKey))
            return
        }
        tryDeepLKeys(keys, text: text, target: target, completion: completion)
    }

    /// Try keys in order; on quota / auth / rate-limit advance to the next.
    private func tryDeepLKeys(
        _ keys: [String],
        text: String,
        target: String,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        guard let key = keys.first else {
            completion(.failure(TranslationError.noDeepLKey))
            return
        }
        let rest = Array(keys.dropFirst())
        deepLRequest(text: text, target: target, apiKey: key) { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let s):
                completion(.success(s))
            case .failure(let e):
                if !rest.isEmpty, Self.shouldRotateDeepLKey(e) {
                    AppLog.log("Translate", "DeepL key failed, trying next (\(rest.count) left)")
                    self.tryDeepLKeys(rest, text: text, target: target, completion: completion)
                } else if !rest.isEmpty {
                    // Network/parse: still try remaining keys once
                    self.tryDeepLKeys(rest, text: text, target: target, completion: completion)
                } else {
                    completion(.failure(e))
                }
            }
        }
    }

    private static func shouldRotateDeepLKey(_ error: Error) -> Bool {
        if case TranslationError.rateLimited = error { return true }
        if case TranslationError.noDeepLKey = error { return true }
        let msg = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        let upper = msg.uppercased()
        return upper.contains("403")
            || upper.contains("456")
            || upper.contains("QUOTA")
            || upper.contains("AUTH")
            || upper.contains("FORBIDDEN")
    }

    private func deepLRequest(
        text: String,
        target: String,
        apiKey: String,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let free = apiKey.hasSuffix(":fx")
        let base = free
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate"
        guard let url = URL(string: base) else {
            completion(.failure(TranslationError.failed("Bad DeepL URL")))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("DeepL-Auth-Key \(apiKey)", forHTTPHeaderField: "Authorization")
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        let tl = TranslationLanguageNorm.forDeepL(target)
        // Omit source_lang → DeepL auto-detects source language.
        let body = "text=\(text.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? text)&target_lang=\(tl)"
        req.httpBody = body.data(using: .utf8)
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            if code == 429 {
                completion(.failure(TranslationError.rateLimited(.deepL)))
                return
            }
            if code == 403 || code == 456 {
                completion(.failure(TranslationError.failed("DeepL HTTP \(code)")))
                return
            }
            guard let data,
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let translations = obj["translations"] as? [[String: Any]],
                  let first = translations.first,
                  let translated = first["text"] as? String else {
                completion(.failure(TranslationError.failed("player.translate.error.deepLParse".localized)))
                return
            }
            completion(.success(translated))
        }.resume()
    }
}
