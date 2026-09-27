namespace Opaline.Core.Config;

/// <summary>
/// User-managed secrets (solver URL, DeepL keys) with masked display helpers.
/// Stored under LocalApplicationData — not source-committed.
/// </summary>
public static class AppSecrets
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Opaline", "secrets.json");

    private static Dictionary<string, string> _map = Load();

    public static string SolverBaseUrl
    {
        get => Get("solverBaseUrl", AppUrls.SolverServer.BaseUrl);
        set
        {
            Set("solverBaseUrl", value);
            if (!string.IsNullOrWhiteSpace(value))
                AppUrls.SolverServer.BaseUrl = value.Trim();
        }
    }

    /// <summary>First DeepL key or empty. Setting accepts multi-line / comma-separated keys.</summary>
    public static string DeepLApiKey
    {
        get => DeepLApiKeys.FirstOrDefault() ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Set("deeplApiKeys", "");
                Set("deeplApiKey", "");
                return;
            }
            var keys = value.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Set("deeplApiKeys", string.Join("\n", keys.Distinct()));
            Set("deeplApiKey", keys.FirstOrDefault() ?? "");
        }
    }

    public static IReadOnlyList<string> DeepLApiKeys
    {
        get
        {
            var blob = Get("deeplApiKeys", "");
            if (string.IsNullOrWhiteSpace(blob))
            {
                var single = Get("deeplApiKey", "");
                return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single.Trim() };
            }
            return blob.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public static string Mask(string? secret, int visibleTail = 4)
    {
        if (string.IsNullOrEmpty(secret)) return "（未设置）";
        if (secret.Length <= 8) return "••••••••";
        return secret[..4] + "••••" + secret[^Math.Min(visibleTail, secret.Length)..];
    }

    public static string MaskDeepLSummary()
    {
        var keys = DeepLApiKeys;
        if (keys.Count == 0) return "（未设置）";
        if (keys.Count == 1) return Mask(keys[0]);
        return $"{keys.Count} 个 Key · {Mask(keys[0])}";
    }

    private static string Get(string k, string fallback)
        => _map.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    private static void Set(string k, string v)
    {
        _map[k] = v;
        Save();
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(Root)) return new Dictionary<string, string>();
            var json = File.ReadAllText(Root);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Root)!);
            File.WriteAllText(Root, System.Text.Json.JsonSerializer.Serialize(_map));
        }
        catch { /* ignore */ }
    }
}
