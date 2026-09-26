using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Opaline.Core.Services.Translation;

/// <summary>
/// Harbor-style translation chain: Google (free gtx) → MyMemory → Lingva.
/// Rate-limit cooldown and simple result cache.
/// Reference: https://github.com/zongai/Harbor TranslationServices / TranslationCoordinator
/// </summary>
public sealed class TranslationService
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly ConcurrentDictionary<TranslationEngine, DateTimeOffset> _limitedUntil = new();
    private static readonly SemaphoreSlim GoogleGate = new(4, 4);

    public TranslationService(HttpClient http) => _http = http;

    public IReadOnlyList<TranslationEngine> DefaultChain { get; } =
        new[] { TranslationEngine.Google, TranslationEngine.MyMemory, TranslationEngine.Lingva };

    public async Task<string> TranslateAsync(
        string text,
        string targetLang = "zh-CN",
        IReadOnlyList<TranslationEngine>? chain = null,
        CancellationToken ct = default)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";

        var cacheKey = $"{targetLang}|{trimmed}";
        if (_cache.TryGetValue(cacheKey, out var hit)) return hit;

        var engines = EffectiveChain(chain ?? DefaultChain);
        Exception? last = null;
        foreach (var engine in engines)
        {
            try
            {
                var result = engine switch
                {
                    TranslationEngine.Google => await GoogleTranslateAsync(trimmed, targetLang, ct).ConfigureAwait(false),
                    TranslationEngine.MyMemory => await MyMemoryTranslateAsync(trimmed, targetLang, ct).ConfigureAwait(false),
                    TranslationEngine.Lingva => await LingvaTranslateAsync(trimmed, targetLang, ct).ConfigureAwait(false),
                    TranslationEngine.DeepL => await DeepLTranslateAsync(trimmed, targetLang, ct).ConfigureAwait(false),
                    _ => throw new TranslationException($"Unknown engine {engine}")
                };
                if (!string.IsNullOrWhiteSpace(result))
                {
                    _cache[cacheKey] = result;
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                if (IsRateLimited(ex))
                    MarkLimited(engine, minutes: 5);
            }
        }
        throw last ?? new TranslationException("No translation engine available");
    }

    public async Task<IReadOnlyList<string>> TranslateManyAsync(
        IReadOnlyList<string> texts,
        string targetLang = "zh-CN",
        CancellationToken ct = default)
    {
        var results = new string[texts.Count];
        // modest parallelism
        await Parallel.ForEachAsync(
            Enumerable.Range(0, texts.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct },
            async (i, token) =>
            {
                results[i] = await TranslateAsync(texts[i], targetLang, ct: token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        return results;
    }

    private List<TranslationEngine> EffectiveChain(IReadOnlyList<TranslationEngine> configured)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<TranslationEngine>();
        var seen = new HashSet<TranslationEngine>();
        foreach (var e in configured)
        {
            if (!seen.Add(e)) continue;
            if (_limitedUntil.TryGetValue(e, out var until) && until > now) continue;
            list.Add(e);
        }
        if (list.Count == 0)
            list.AddRange(configured.Distinct());
        return list;
    }

    private void MarkLimited(TranslationEngine e, double minutes)
        => _limitedUntil[e] = DateTimeOffset.UtcNow.AddMinutes(minutes);

    private static bool IsRateLimited(Exception ex)
        => ex.Message.Contains("429", StringComparison.Ordinal)
           || ex.Message.Contains("限流", StringComparison.Ordinal);

    // ── Google free gtx (Harbor GoogleTranslate) ─────────────────────────

    private async Task<string> GoogleTranslateAsync(string text, string targetLang, CancellationToken ct)
    {
        await GoogleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tl = TargetLanguages.NormalizeGoogle(targetLang);
            if (Encoding.UTF8.GetByteCount(text) < 1800)
            {
                try { return await GoogleRequestAsync(text, tl, useGet: true, ct).ConfigureAwait(false); }
                catch { /* fall through to POST */ }
            }
            return await GoogleRequestAsync(text, tl, useGet: false, ct).ConfigureAwait(false);
        }
        finally { GoogleGate.Release(); }
    }

    private async Task<string> GoogleRequestAsync(string text, string targetLang, bool useGet, CancellationToken ct)
    {
        HttpRequestMessage req;
        if (useGet)
        {
            var qs =
                "client=gtx&sl=auto&tl=" + Uri.EscapeDataString(targetLang) +
                "&dt=t&q=" + Uri.EscapeDataString(text);
            req = new HttpRequestMessage(HttpMethod.Get,
                "https://translate.googleapis.com/translate_a/single?" + qs);
        }
        else
        {
            req = new HttpRequestMessage(HttpMethod.Post,
                "https://translate.googleapis.com/translate_a/single");
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = "gtx",
                ["sl"] = "auto",
                ["tl"] = targetLang,
                ["dt"] = "t",
                ["q"] = text
            });
            req.Content = body;
        }
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests || raw.TrimStart().StartsWith("<!", StringComparison.Ordinal))
            throw new TranslationException("Google 限流 (429)，请稍后再试");
        if (!resp.IsSuccessStatusCode)
            throw new TranslationException($"Google 翻译失败 ({(int)resp.StatusCode})");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new TranslationException("解析 Google 响应失败");
        var segments = root[0];
        if (segments.ValueKind != JsonValueKind.Array)
            throw new TranslationException("解析 Google 响应失败");
        var sb = new StringBuilder();
        foreach (var seg in segments.EnumerateArray())
        {
            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 &&
                seg[0].ValueKind == JsonValueKind.String)
                sb.Append(seg[0].GetString());
        }
        var result = sb.ToString();
        if (string.IsNullOrWhiteSpace(result))
            throw new TranslationException("Google 返回空译文");
        return result;
    }

    // ── MyMemory free ────────────────────────────────────────────────────

    private async Task<string> MyMemoryTranslateAsync(string text, string targetLang, CancellationToken ct)
    {
        var chunk = text.Length > 450 ? text[..450] : text;
        var tl = TargetLanguages.NormalizeMyMemory(targetLang);
        var url =
            $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(chunk)}&langpair=Autodetect|{Uri.EscapeDataString(tl)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new TranslationException($"MyMemory 失败 ({(int)resp.StatusCode})");
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("responseData", out var rd) ||
            !rd.TryGetProperty("translatedText", out var tt))
            throw new TranslationException("MyMemory 解析失败");
        var outText = tt.GetString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(outText))
            throw new TranslationException("MyMemory 返回空译文");
        if (outText.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            throw new TranslationException("MyMemory 额度可能已用尽");
        return outText;
    }

    // ── Lingva public instances ──────────────────────────────────────────

    private static readonly string[] LingvaHosts =
    {
        "https://lingva.ml",
        "https://lingva.thedaviddelta.com",
        "https://translate.plausibility.cloud"
    };

    private async Task<string> LingvaTranslateAsync(string text, string targetLang, CancellationToken ct)
    {
        var tl = targetLang.ToLowerInvariant() switch
        {
            "zh-cn" or "zh" or "zh-hans" => "zh",
            "zh-tw" or "zh-hant" => "zh_HANT",
            _ => targetLang.Split('-')[0]
        };
        Exception? last = null;
        foreach (var host in LingvaHosts)
        {
            try
            {
                if (text.Length > 300)
                    return await LingvaPostAsync(host, tl, text, ct).ConfigureAwait(false);
                try { return await LingvaGetAsync(host, tl, text, ct).ConfigureAwait(false); }
                catch { return await LingvaPostAsync(host, tl, text, ct).ConfigureAwait(false); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }
        throw last ?? new TranslationException("Lingva 暂不可用");
    }

    private async Task<string> LingvaGetAsync(string host, string target, string query, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query).Replace("%20", "+");
        // path-style: /api/v1/auto/{target}/{query}
        var url = $"{host}/api/v1/auto/{target}/{Uri.EscapeDataString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        return await LingvaParseAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<string> LingvaPostAsync(string host, string target, string query, CancellationToken ct)
    {
        var url = $"{host}/api/v1/auto/{target}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { query }),
            Encoding.UTF8, "application/json");
        return await LingvaParseAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<string> LingvaParseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new TranslationException($"Lingva 失败 ({(int)resp.StatusCode})");
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("translation", out var tr))
        {
            var s = tr.GetString()?.Trim() ?? "";
            if (!string.IsNullOrEmpty(s)) return s;
        }
        throw new TranslationException("Lingva 解析失败");
    }


    // ── DeepL (Harbor DeepLTranslate; optional key via OPALINE_DEEPL_KEY) ─

    private async Task<string> DeepLTranslateAsync(string text, string targetLang, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable("OPALINE_DEEPL_KEY")
               ?? Environment.GetEnvironmentVariable("DEEPL_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new TranslationException("未配置 DeepL API Key（OPALINE_DEEPL_KEY）");

        var isFree = key.EndsWith(":fx", StringComparison.Ordinal);
        var url = isFree
            ? "https://api-free.deepl.com/v2/translate"
            : "https://api.deepl.com/v2/translate";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + key);
        var body = new
        {
            text = new[] { text },
            target_lang = TargetLanguages.NormalizeDeepL(targetLang)
        };
        req.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new TranslationException($"DeepL 失败 ({(int)resp.StatusCode}): {raw[..Math.Min(120, raw.Length)]}");

        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var translations = doc.RootElement.GetProperty("translations");
        if (translations.GetArrayLength() == 0)
            throw new TranslationException("DeepL 返回空译文");
        return translations[0].GetProperty("text").GetString() ?? "";
    }

    /// <summary>Strip VTT/timing lines then translate remaining text blocks.</summary>
    public async Task<string> TranslateCaptionAsync(string rawCaption, string targetLang, CancellationToken ct = default)
    {
        var textLines = rawCaption.Split('\n')
            .Select(l => l.Trim())
            .Where(l =>
                !string.IsNullOrEmpty(l) &&
                !l.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) &&
                !l.Contains("-->") &&
                !Regex.IsMatch(l, @"^\d+$") &&
                !l.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (textLines.Count == 0) return "";
        // batch into ~800 char chunks
        var chunks = new List<string>();
        var buf = new StringBuilder();
        foreach (var line in textLines)
        {
            if (buf.Length + line.Length > 800)
            {
                chunks.Add(buf.ToString());
                buf.Clear();
            }
            if (buf.Length > 0) buf.Append(' ');
            buf.Append(line);
        }
        if (buf.Length > 0) chunks.Add(buf.ToString());

        var translated = await TranslateManyAsync(chunks, targetLang, ct).ConfigureAwait(false);
        return string.Join("\n", translated);
    }
}
