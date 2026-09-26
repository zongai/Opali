import Foundation

/// User settings for in-app translation (Harbor-style engines).
enum TranslationPreferences {
    enum Engine: String, CaseIterable {
        case google
        case myMemory
        case lingva
        case deepL

        var displayNameKey: String {
            switch self {
            case .google: return "settings.translation.engine.google"
            case .myMemory: return "settings.translation.engine.myMemory"
            case .lingva: return "settings.translation.engine.lingva"
            case .deepL: return "settings.translation.engine.deepL"
            }
        }

        var displayName: String { displayNameKey.localized }

        var asServiceEngine: TranslationEngine {
            switch self {
            case .google: return .google
            case .myMemory: return .myMemory
            case .lingva: return .lingva
            case .deepL: return .deepL
            }
        }
    }

    /// Target language BCP-47-ish code; nil = follow app language.
    static var targetLanguageOverride: String? {
        get {
            UserDefaults.standard.string(forKey: UserDefaultsKeys.Translation.targetLanguage)
        }
        set {
            if let newValue {
                UserDefaults.standard.set(newValue, forKey: UserDefaultsKeys.Translation.targetLanguage)
            } else {
                UserDefaults.standard.removeObject(forKey: UserDefaultsKeys.Translation.targetLanguage)
            }
        }
    }

    static var effectiveTargetLanguage: String {
        if let o = targetLanguageOverride, !o.isEmpty {
            return normalizeTarget(o)
        }
        return TranslationService.preferredTargetFromAppLanguage
    }

    static var preferredEngine: Engine {
        get {
            let raw = UserDefaults.standard.string(forKey: UserDefaultsKeys.Translation.preferredEngine)
            return Engine(rawValue: raw ?? "") ?? .google
        }
        set {
            UserDefaults.standard.set(newValue.rawValue, forKey: UserDefaultsKeys.Translation.preferredEngine)
        }
    }


    /// Ordered chain: preferred first, then the rest (DeepL last unless preferred).
    static var engineChain: [TranslationEngine] {
        let preferred = preferredEngine.asServiceEngine
        var rest = TranslationEngine.allCases.filter { $0 != preferred }
        if preferred != .deepL {
            rest = rest.filter { $0 != .deepL } + [.deepL]
        }
        return [preferred] + rest
    }

    // MARK: - DeepL keys (multi-key)

    /// All configured DeepL API keys (order = try order). Free keys end with `:fx`.
    /// Stored as an array; migrates the legacy single-string key on first read.
    static var deepLAPIKeys: [String] {
        get {
            let defaults = UserDefaults.standard
            if let arr = defaults.stringArray(forKey: UserDefaultsKeys.Translation.deepLAPIKeys) {
                return sanitizeKeys(arr)
            }
            // Migrate legacy single key
            if let legacy = defaults.string(forKey: UserDefaultsKeys.Translation.deepLAPIKey)?
                .trimmingCharacters(in: .whitespacesAndNewlines),
               !legacy.isEmpty {
                let keys = sanitizeKeys([legacy])
                defaults.set(keys, forKey: UserDefaultsKeys.Translation.deepLAPIKeys)
                defaults.removeObject(forKey: UserDefaultsKeys.Translation.deepLAPIKey)
                return keys
            }
            // Environment fallbacks (not persisted)
            var env: [String] = []
            for name in ["OPALINE_DEEPL_KEY", "DEEPL_API_KEY", "OPALINE_DEEPL_KEYS"] {
                if let v = ProcessInfo.processInfo.environment[name], !v.isEmpty {
                    env.append(contentsOf: parseKeyBlob(v))
                }
            }
            return sanitizeKeys(env)
        }
        set {
            let keys = sanitizeKeys(newValue)
            let defaults = UserDefaults.standard
            if keys.isEmpty {
                defaults.removeObject(forKey: UserDefaultsKeys.Translation.deepLAPIKeys)
            } else {
                defaults.set(keys, forKey: UserDefaultsKeys.Translation.deepLAPIKeys)
            }
            defaults.removeObject(forKey: UserDefaultsKeys.Translation.deepLAPIKey)
        }
    }

    /// First configured key (compatibility).
    static var deepLAPIKey: String? {
        get { deepLAPIKeys.first }
        set {
            if let newValue, !newValue.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                var keys = deepLAPIKeys
                if keys.isEmpty {
                    deepLAPIKeys = sanitizeKeys([newValue])
                } else {
                    keys[0] = newValue.trimmingCharacters(in: .whitespacesAndNewlines)
                    deepLAPIKeys = keys
                }
            } else {
                deepLAPIKeys = []
            }
        }
    }

    /// Settings detail text: count + masked first key.
    static var deepLKeyDisplay: String {
        let keys = deepLAPIKeys
        guard !keys.isEmpty else {
            return "settings.translation.deepL.notSet".localized
        }
        let first = keys[0]
        let mask: String
        if first.count <= 8 {
            mask = "••••"
        } else {
            mask = String(first.prefix(4)) + "••••" + String(first.suffix(4))
        }
        if keys.count == 1 {
            return mask
        }
        return "settings.translation.deepL.keyCount".localized(with: keys.count) + " · " + mask
    }

    /// Text blob shown in the multi-key editor (one key per line).
    static var deepLKeysEditorText: String {
        deepLAPIKeys.joined(separator: "
")
    }

    static func setDeepLKeys(fromEditorText text: String?) {
        deepLAPIKeys = parseKeyBlob(text ?? "")
    }

    /// Split by newline / comma / semicolon / whitespace runs.
    static func parseKeyBlob(_ text: String) -> [String] {
        let normalized = text
            .replacingOccurrences(of: ",", with: "
")
            .replacingOccurrences(of: ";", with: "
")
        return normalized
            .components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
    }

    private static func sanitizeKeys(_ keys: [String]) -> [String] {
        var seen = Set<String>()
        var out: [String] = []
        for raw in keys {
            let k = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !k.isEmpty, seen.insert(k).inserted else { continue }
            out.append(k)
        }
        return out
    }


    static var targetDisplayName: String {
        if targetLanguageOverride == nil {
            return "settings.language.system".localized
                + " (\(effectiveTargetLanguage))"
        }
        return languageDisplayName(effectiveTargetLanguage)
    }

    static func languageDisplayName(_ code: String) -> String {
        let locale = Locale(identifier: AppLanguage.effective.rawValue)
        let base = code.split(separator: "-").first.map(String.init) ?? code
        if let name = locale.localizedString(forLanguageCode: base) {
            if code.lowercased().contains("tw") || code.lowercased().contains("hant") {
                return name + " (繁體)"
            }
            if code.lowercased().contains("cn") || code.lowercased().contains("hans") {
                return name + " (简体)"
            }
            return name.capitalized
        }
        return code
    }

    static func normalizeTarget(_ code: String) -> String {
        switch code.lowercased() {
        case "zh", "zh-hans", "zh-cn": return "zh-CN"
        case "zh-hant", "zh-tw": return "zh-TW"
        default: return code
        }
    }

    /// Curated target list for the picker.
    static let targetOptions: [(code: String?, titleKey: String)] = [
        (nil, "settings.language.system"),
        ("zh-CN", "settings.translation.lang.zhHans"),
        ("zh-TW", "settings.translation.lang.zhHant"),
        ("en", "settings.translation.lang.en"),
        ("ja", "settings.translation.lang.ja"),
        ("ko", "settings.translation.lang.ko"),
        ("es", "settings.translation.lang.es"),
        ("fr", "settings.translation.lang.fr"),
        ("de", "settings.translation.lang.de"),
        ("ru", "settings.translation.lang.ru"),
        ("vi", "settings.translation.lang.vi"),
        ("id", "settings.translation.lang.id")
    ]
}

enum PlaybackSpeedPreference {
    static let steps: [Float] = [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0]

    static var defaultSpeed: Float {
        get {
            let v = UserDefaults.standard.object(forKey: UserDefaultsKeys.Player.defaultSpeed) as? Float
            return v ?? 1.0
        }
        set {
            let snapped = steps.min(by: { abs($0 - newValue) < abs($1 - newValue) }) ?? 1.0
            UserDefaults.standard.set(snapped, forKey: UserDefaultsKeys.Player.defaultSpeed)
        }
    }

    static var displayName: String {
        let s = defaultSpeed
        return s == 1.0
            ? "player.speed.normal".localized
            : String(format: "%.2gx", s)
    }
}
