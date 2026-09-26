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
    
    /// DeepL API key from Settings (optional). Free keys end with `:fx`.
    static var deepLAPIKey: String? {
        get {
            let s = UserDefaults.standard.string(forKey: UserDefaultsKeys.Translation.deepLAPIKey)?
                .trimmingCharacters(in: .whitespacesAndNewlines)
            return (s?.isEmpty == false) ? s : nil
        }
        set {
            let trimmed = newValue?.trimmingCharacters(in: .whitespacesAndNewlines)
            if let trimmed, !trimmed.isEmpty {
                UserDefaults.standard.set(trimmed, forKey: UserDefaultsKeys.Translation.deepLAPIKey)
            } else {
                UserDefaults.standard.removeObject(forKey: UserDefaultsKeys.Translation.deepLAPIKey)
            }
        }
    }

    static var deepLKeyDisplay: String {
        guard let key = deepLAPIKey, !key.isEmpty else {
            return "settings.translation.deepL.notSet".localized
        }
        if key.count <= 8 { return "••••" }
        return String(key.prefix(4)) + "••••" + String(key.suffix(4))
    }

static var engineChain: [TranslationEngine] {
        let preferred = preferredEngine.asServiceEngine
        var rest = TranslationEngine.allCases.filter { $0 != preferred }
        // Keep DeepL at end of rest if not preferred
        if preferred != .deepL {
            rest = rest.filter { $0 != .deepL } + [.deepL]
        }
        return [preferred] + rest
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
