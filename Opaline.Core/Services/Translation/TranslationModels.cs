namespace Opaline.Core.Services.Translation;

/// <summary>Harbor TranslationEngine order: Google → MyMemory → Lingva (+ optional DeepL).</summary>
public enum TranslationEngine
{
    Google,
    MyMemory,
    Lingva,
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
        "zh" or "zh-hans" => "zh-CN",
        "zh-hant" => "zh-TW",
        _ => code
    };

    public static string NormalizeMyMemory(string code) => code.ToLowerInvariant() switch
    {
        "zh" or "zh-hans" or "zh-cn" => "zh-CN",
        "zh-hant" or "zh-tw" => "zh-TW",
        _ => code
    };

    /// <summary>DeepL uses uppercase codes; zh-CN → ZH.</summary>
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
