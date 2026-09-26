import Foundation

// MARK: - Harbor-aligned free engines (feature/epub-opds TranslationServices)
// Reference: https://github.com/zongai/Harbor feature/epub-opds

/// Yandex web API (no key) — session SID + tr.json/translate.
enum YandexTranslateEngine {
    private static let sessionURL = URL(string: "https://translate.yandex.ru/props/api/v1.0/sessions")!
    private static let translateURL = URL(string: "https://translate.yandex.net/api/v1/tr.json/translate")!
    private static let origin = "https://translate.yandex.ru"
    private static let ua =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"

    private static var sid: String?
    private static var sidExpires: Date = .distantPast
    private static let lock = NSLock()
    private static let session: URLSession = {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 18
        return URLSession(configuration: cfg)
    }()

    static func translate(
        _ text: String,
        target: String,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            completion(.success(""))
            return
        }
        let tl = normalizeLang(target)
        fetchSID { result in
            switch result {
            case .failure(let e):
                completion(.failure(e))
            case .success(let sid):
                translateWithSID(trimmed, targetLang: tl, sid: sid, retry: true, completion: completion)
            }
        }
    }

    private static func normalizeLang(_ lang: String) -> String {
        let l = lang.lowercased()
        if l.hasPrefix("zh") { return "zh" }
        if l.hasPrefix("en") { return "en" }
        if l.hasPrefix("ja") { return "ja" }
        if l.hasPrefix("ko") { return "ko" }
        if l.hasPrefix("fr") { return "fr" }
        if l.hasPrefix("de") { return "de" }
        if l.hasPrefix("es") { return "es" }
        if l.hasPrefix("ru") { return "ru" }
        if l.hasPrefix("vi") { return "vi" }
        return String(l.prefix(2))
    }

    private static func fetchSID(completion: @escaping (Result<String, Error>) -> Void) {
        lock.lock()
        if let sid, sidExpires > Date() {
            lock.unlock()
            completion(.success(sid))
            return
        }
        lock.unlock()

        var comps = URLComponents(url: sessionURL, resolvingAgainstBaseURL: false)!
        comps.queryItems = [
            URLQueryItem(name: "srv", value: "tr-text"),
            URLQueryItem(name: "yu", value: String(Int.random(in: 1_000_000_000_000...9_999_999_999_999_999))),
            URLQueryItem(name: "yum", value: String(Int(Date().timeIntervalSince1970 * 1_000_000)))
        ]
        guard let url = comps.url else {
            completion(.failure(TranslationError.failed("Yandex session URL")))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue(ua, forHTTPHeaderField: "User-Agent")
        req.setValue(origin, forHTTPHeaderField: "Origin")
        req.setValue(origin + "/", forHTTPHeaderField: "Referer")
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let code = (response as? HTTPURLResponse)?.statusCode ?? 0
            guard (200...299).contains(code),
                  let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let sessionObj = json["session"] as? [String: Any],
                  let id = sessionObj["id"] as? String, !id.isEmpty else {
                completion(.failure(TranslationError.failed("Yandex session parse")))
                return
            }
            let created = (sessionObj["creationTimestamp"] as? Double) ?? Date().timeIntervalSince1970
            let maxAge = (sessionObj["maxAge"] as? Double) ?? 3600
            lock.lock()
            sid = id
            sidExpires = Date(timeIntervalSince1970: created + maxAge - 60)
            lock.unlock()
            completion(.success(id))
        }.resume()
    }

    private static func invalidateSID() {
        lock.lock()
        sid = nil
        sidExpires = .distantPast
        lock.unlock()
    }

    private static func translateWithSID(
        _ text: String,
        targetLang: String,
        sid: String,
        retry: Bool,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        var comps = URLComponents(url: translateURL, resolvingAgainstBaseURL: false)!
        comps.queryItems = [
            URLQueryItem(name: "srv", value: "tr-text"),
            URLQueryItem(name: "sid", value: "\(sid)-5-0"),
            URLQueryItem(name: "target_lang", value: targetLang),
            URLQueryItem(name: "reason", value: "paste"),
            URLQueryItem(name: "format", value: "text"),
            URLQueryItem(name: "strategy", value: "0"),
            URLQueryItem(name: "disable_cache", value: "false"),
            URLQueryItem(name: "ajax", value: "1")
        ]
        guard let url = comps.url else {
            completion(.failure(TranslationError.failed("Yandex URL")))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue(ua, forHTTPHeaderField: "User-Agent")
        req.setValue(origin, forHTTPHeaderField: "Origin")
        req.setValue(origin + "/", forHTTPHeaderField: "Referer")
        var body = URLComponents()
        body.queryItems = [
            URLQueryItem(name: "options", value: "0"),
            URLQueryItem(name: "text", value: String(text.prefix(600)))
        ]
        req.httpBody = body.percentEncodedQuery?.data(using: .utf8)
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let http = (response as? HTTPURLResponse)?.statusCode ?? 0
            if (http == 401 || http == 403), retry {
                invalidateSID()
                fetchSID { r in
                    switch r {
                    case .failure(let e): completion(.failure(e))
                    case .success(let newSid):
                        translateWithSID(text, targetLang: targetLang, sid: newSid, retry: false, completion: completion)
                    }
                }
                return
            }
            guard let data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                completion(.failure(TranslationError.failed("Yandex parse")))
                return
            }
            let code = json["code"] as? Int ?? 0
            if (code == 401 || code == 403), retry {
                invalidateSID()
                fetchSID { r in
                    switch r {
                    case .failure(let e): completion(.failure(e))
                    case .success(let newSid):
                        translateWithSID(text, targetLang: targetLang, sid: newSid, retry: false, completion: completion)
                    }
                }
                return
            }
            if let arr = json["text"] as? [String] {
                completion(.success(arr.joined()))
                return
            }
            completion(.failure(TranslationError.failed("Yandex empty")))
        }.resume()
    }
}

/// Bing Translator web API (no key) — scrape AbusePreventionHelper + ttranslatev3.
enum AzureBingTranslateEngine {
    private static let pageURL = URL(string: "https://www.bing.com/translator")!
    private static let ua =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"

    private struct Auth {
        var ig: String
        var iid: String
        var key: String
        var token: String
        var expiresAt: Date
        var host: String
    }

    private static var cached: Auth?
    private static let lock = NSLock()
    private static let session: URLSession = {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 20
        return URLSession(configuration: cfg)
    }()

    static func translate(
        _ text: String,
        target: String,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            completion(.success(""))
            return
        }
        let tl = normalizeLang(target)
        fetchAuth { result in
            switch result {
            case .failure(let e):
                completion(.failure(e))
            case .success(let auth):
                translateChunk(String(trimmed.prefix(1000)), to: tl, auth: auth, retry: true, completion: completion)
            }
        }
    }

    private static func normalizeLang(_ lang: String) -> String {
        switch lang.lowercased() {
        case "zh", "zh-cn", "zh-hans": return "zh-Hans"
        case "zh-tw", "zh-hk", "zh-hant": return "zh-Hant"
        case "en", "en-us", "en-gb": return "en"
        case "ja", "ja-jp": return "ja"
        case "ko", "ko-kr": return "ko"
        case "fr", "fr-fr": return "fr"
        case "de", "de-de": return "de"
        case "es", "es-es": return "es"
        case "ru", "ru-ru": return "ru"
        case "vi", "vi-vn": return "vi"
        default:
            if let dash = lang.firstIndex(of: "-") {
                return String(lang[..<dash])
            }
            return lang
        }
    }

    private static func invalidateAuth() {
        lock.lock()
        cached = nil
        lock.unlock()
    }

    private static func fetchAuth(completion: @escaping (Result<Auth, Error>) -> Void) {
        lock.lock()
        if let cached, cached.expiresAt > Date() {
            lock.unlock()
            completion(.success(cached))
            return
        }
        lock.unlock()

        var req = URLRequest(url: pageURL)
        req.setValue(ua, forHTTPHeaderField: "User-Agent")
        req.setValue("text/html", forHTTPHeaderField: "Accept")
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let http = response as? HTTPURLResponse
            let code = http?.statusCode ?? 0
            guard (200...299).contains(code), let data else {
                completion(.failure(TranslationError.failed("Bing page \(code)")))
                return
            }
            let html = String(data: data, encoding: .utf8) ?? ""
            let ns = html as NSString
            let full = NSRange(location: 0, length: ns.length)
            guard let abuseRe = try? NSRegularExpression(
                    pattern: #"params_AbusePreventionHelper\s*=\s*\[\s*(\d+)\s*,\s*"([^"]+)"\s*,\s*(\d+)\s*\]"#
                ),
                let abuse = abuseRe.firstMatch(in: html, range: full),
                abuse.numberOfRanges >= 4,
                let igRe = try? NSRegularExpression(pattern: #"IG\s*:\s*"([A-Fa-f0-9]+)""#),
                let igM = igRe.firstMatch(in: html, range: full),
                igM.numberOfRanges >= 2,
                let iidRe = try? NSRegularExpression(pattern: #"data-iid\s*=\s*"([^"]+)""#),
                let iidM = iidRe.firstMatch(in: html, range: full),
                iidM.numberOfRanges >= 2
            else {
                completion(.failure(TranslationError.failed("Bing auth parse")))
                return
            }
            let key = ns.substring(with: abuse.range(at: 1))
            let token = ns.substring(with: abuse.range(at: 2))
            let ttlMs = Double(ns.substring(with: abuse.range(at: 3))) ?? 3_600_000
            let ig = ns.substring(with: igM.range(at: 1))
            let iid = ns.substring(with: iidM.range(at: 1))
            var host = "www.bing.com"
            if let final = http?.url?.host, final.hasSuffix("bing.com") {
                host = final
            }
            let auth = Auth(
                ig: ig,
                iid: iid,
                key: key,
                token: token,
                expiresAt: Date().addingTimeInterval(max(ttlMs - 60_000, 0) / 1000.0),
                host: host
            )
            lock.lock()
            cached = auth
            lock.unlock()
            completion(.success(auth))
        }.resume()
    }

    private static func translateChunk(
        _ text: String,
        to: String,
        auth: Auth,
        retry: Bool,
        completion: @escaping (Result<String, Error>) -> Void
    ) {
        var comps = URLComponents()
        comps.scheme = "https"
        comps.host = auth.host
        comps.path = "/ttranslatev3"
        comps.queryItems = [
            URLQueryItem(name: "isVertical", value: "1"),
            URLQueryItem(name: "IG", value: auth.ig),
            URLQueryItem(name: "IID", value: auth.iid)
        ]
        guard let url = comps.url else {
            completion(.failure(TranslationError.failed("Bing URL")))
            return
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        req.setValue(ua, forHTTPHeaderField: "User-Agent")
        req.setValue("https://www.bing.com/translator", forHTTPHeaderField: "Referer")
        var body = URLComponents()
        body.queryItems = [
            URLQueryItem(name: "fromLang", value: "auto-detect"),
            URLQueryItem(name: "text", value: text),
            URLQueryItem(name: "to", value: to),
            URLQueryItem(name: "token", value: auth.token),
            URLQueryItem(name: "key", value: auth.key)
        ]
        req.httpBody = body.percentEncodedQuery?.data(using: .utf8)
        session.dataTask(with: req) { data, response, error in
            if let error {
                completion(.failure(error))
                return
            }
            let http = (response as? HTTPURLResponse)?.statusCode ?? 0
            if !(200...299).contains(http), retry {
                invalidateAuth()
                fetchAuth { r in
                    switch r {
                    case .failure(let e): completion(.failure(e))
                    case .success(let a):
                        translateChunk(text, to: to, auth: a, retry: false, completion: completion)
                    }
                }
                return
            }
            guard let data else {
                completion(.failure(TranslationError.failed("Bing empty")))
                return
            }
            if let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
               let code = obj["statusCode"] as? Int, code != 200 {
                if code == 205, retry {
                    invalidateAuth()
                    fetchAuth { r in
                        switch r {
                        case .failure(let e): completion(.failure(e))
                        case .success(let a):
                            translateChunk(text, to: to, auth: a, retry: false, completion: completion)
                        }
                    }
                    return
                }
                completion(.failure(TranslationError.failed("Bing status \(code)")))
                return
            }
            if let arr = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]],
               let first = arr.first,
               let translations = first["translations"] as? [[String: Any]],
               let t = translations.first?["text"] as? String {
                completion(.success(t))
                return
            }
            completion(.failure(TranslationError.failed("Bing parse")))
        }.resume()
    }
}
