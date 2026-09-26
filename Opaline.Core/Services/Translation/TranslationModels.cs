namespace Opaline.Core.Services.Translation;

public enum TranslationEngine
{
    Google,
    MyMemory,
    Lingva
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
}
