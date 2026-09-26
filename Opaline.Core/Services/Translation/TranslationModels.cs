namespace Opaline.Core.Services.Translation;

/// <summary>
/// Harbor feature/epub-opds engine order:
/// Google → MyMemory → Lingva → Yandex → Bing → DeepL.
/// </summary>
public enum TranslationEngine
{
    Google,
    MyMemory,
    Lingva,
    Yandex,
    AzureBing,
    DeepL
}

public sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
    public TranslationException(string message, Exception inner) : base(message, inner) { }
}

public static class TargetLanguages
{
    public static IReadOnlyList<(string Code, string Name)> All { get; } = new[]
    {
        ("zh-CN", "中文（简体）"),
        ("zh-TW", "中文（繁體）"),
        ("en", "English"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("es", "Español"),
        ("fr", "Français"),
        ("de", "Deutsch"),
        ("ru", "Русский"),
        ("pt", "Português"),
        ("vi", "Tiếng Việt"),
        ("th", "ไทย"),
        ("id", "Indonesia"),
        ("ar", "العربية"),
        ("hi", "हिन्दी"),
    };

    public static string NormalizeGoogle(string code) => code.ToLowerInvariant() switch
    {
        "zh" or "zh-hans" or "zh-cn" => "zh-CN",
        "zh-hant" or "zh-tw" => "zh-TW",
        _ => code
    };

    public static string NormalizeMyMemory(string code) => NormalizeGoogle(code);

    public static string NormalizeLingva(string code) => code.ToLowerInvariant() switch
    {
        "zh" or "zh-hans" or "zh-cn" => "zh",
        "zh-hant" or "zh-tw" => "zh_HANT",
        _ => code.Replace('-', '_')
    };

    public static string NormalizeYandex(string code)
    {
        var l = code.ToLowerInvariant();
        if (l.StartsWith("zh")) return "zh";
        if (l.StartsWith("en")) return "en";
        if (l.StartsWith("ja")) return "ja";
        if (l.StartsWith("ko")) return "ko";
        return l.Length >= 2 ? l[..2] : l;
    }

    public static string NormalizeBing(string code) => code.ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" => "zh-Hans",
        "zh-tw" or "zh-hk" or "zh-hant" => "zh-Hant",
        "en" or "en-us" or "en-gb" => "en",
        _ => code.Contains('-') ? code.Split('-')[0] : code
    };

    /// <summary>DeepL uses uppercase codes; zh-CN → ZH, zh-TW → ZH-HANT.</summary>
    public static string NormalizeDeepL(string code) => code.ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" => "ZH",
        "zh-tw" or "zh-hant" => "ZH-HANT",
        "en" => "EN",
        "ja" => "JA",
        "ko" => "KO",
        "es" => "ES",
        "fr" => "FR",
        "de" => "DE",
        "ru" => "RU",
        "pt" => "PT",
        "vi" => "VI",
        "id" => "ID",
        "ar" => "AR",
        _ => code.ToUpperInvariant()
    };
}
