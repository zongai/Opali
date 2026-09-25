using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Opaline.Core.Config;

namespace Opaline.Core.Playback;

/// <summary>
/// Solves YouTube player signatures:
/// <list type="bullet">
///   <item><c>n</c> — throttling parameter (always required when present)</item>
///   <item><c>s</c> — signatureCipher challenge (WEB / mweb formats)</item>
/// </list>
/// Primary path: remote solver-server (<c>POST /solve</c>), same as the iOS app.
/// Also scrapes player JS URL from the watch page when STS service has none.
/// </summary>
public sealed class SignatureSolverService
{
    private readonly HttpClient _http;
    private readonly SignatureTimestampService _sts;
    private readonly ConcurrentDictionary<string, string> _nCache = new();
    private readonly ConcurrentDictionary<string, string> _sCache = new();
    private string? _playerJsUrl;
    private readonly object _playerLock = new();

    private static readonly Regex PlayerJsRegex = new(
        @"player\\?/([a-f0-9]{8,}[^""]*?/base\.js)|""jsUrl""\s*:\s*""([^""]+base\.js[^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SignatureSolverService(HttpClient http, SignatureTimestampService sts)
    {
        _http = http;
        _sts = sts;
    }

    public string? PlayerJsUrl
    {
        get { lock (_playerLock) return _playerJsUrl ?? AbsolutePlayerUrl(_sts.JsPath); }
    }

    /// <summary>
    /// Ensure we know the player JS URL (from STS scrape or watch HTML).
    /// </summary>
    public async Task EnsurePlayerJsAsync(string? videoId = null, CancellationToken ct = default)
    {
        if (PlayerJsUrl is not null) return;

        // STS service may already have jsPath
        _ = await _sts.GetAsync(ct).ConfigureAwait(false);
        if (PlayerJsUrl is not null) return;

        if (string.IsNullOrEmpty(videoId)) return;
        try
        {
            var url = $"https://www.youtube.com/watch?v={videoId}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var m = PlayerJsRegex.Match(html);
            if (!m.Success) return;
            var path = m.Groups[1].Success ? "/s/player/" + m.Groups[1].Value : m.Groups[2].Value;
            lock (_playerLock)
                _playerJsUrl = AbsolutePlayerUrl(path);
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>Solve the <c>n</c> throttling parameter.</summary>
    public async Task<string?> SolveNAsync(string n, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(n)) return null;
        if (_nCache.TryGetValue(n, out var cached)) return cached;

        var solved = await RemoteSolveAsync(n: n, s: null, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(solved?.EffectiveN))
        {
            _nCache[n] = solved!.EffectiveN!;
            return solved.EffectiveN;
        }
        return null;
    }

    /// <summary>Solve the <c>s</c> signatureCipher challenge.</summary>
    public async Task<string?> SolveSAsync(string s, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (_sCache.TryGetValue(s, out var cached)) return cached;

        var solved = await RemoteSolveAsync(n: null, s: s, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(solved?.EffectiveSig))
        {
            _sCache[s] = solved!.EffectiveSig!;
            return solved.EffectiveSig;
        }
        return null;
    }

    /// <summary>Solve both challenges in one round-trip when possible.</summary>
    public async Task<(string? N, string? Sig)> SolveBothAsync(
        string? n, string? s, CancellationToken ct = default)
    {
        string? solvedN = null;
        string? solvedS = null;

        if (!string.IsNullOrEmpty(n) && _nCache.TryGetValue(n, out var cn))
            solvedN = cn;
        if (!string.IsNullOrEmpty(s) && _sCache.TryGetValue(s, out var cs))
            solvedS = cs;

        if ((n is null || solvedN is not null) && (s is null || solvedS is not null))
            return (solvedN, solvedS);

        var needN = solvedN is null ? n : null;
        var needS = solvedS is null ? s : null;
        var result = await RemoteSolveAsync(needN, needS, ct).ConfigureAwait(false);
        if (result is null) return (solvedN, solvedS);

        if (!string.IsNullOrEmpty(result.EffectiveN) && !string.IsNullOrEmpty(n))
        {
            _nCache[n] = result.EffectiveN!;
            solvedN = result.EffectiveN;
        }
        if (!string.IsNullOrEmpty(result.EffectiveSig) && !string.IsNullOrEmpty(s))
        {
            _sCache[s] = result.EffectiveSig!;
            solvedS = result.EffectiveSig;
        }
        return (solvedN, solvedS);
    }

    private async Task<SolveResponse?> RemoteSolveAsync(
        string? n, string? s, CancellationToken ct)
    {
        var endpoint = AppUrls.NSolver.Solve;
        if (endpoint is null) return null;

        try
        {
            var body = new SolveRequest
            {
                N = n,
                S = s,
                Signature = s,
                PlayerUrl = PlayerJsUrl
            };
            using var resp = await _http.PostAsJsonAsync(endpoint, body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<SolveResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string? AbsolutePlayerUrl(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        if (!path.StartsWith('/')) path = "/" + path;
        return "https://www.youtube.com" + path;
    }

    private sealed class SolveRequest
    {
        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("s")]
        public string? S { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        [JsonPropertyName("player_url")]
        public string? PlayerUrl { get; set; }
    }

    private sealed class SolveResponse
    {
        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("solved_n")]
        public string? SolvedN { get; set; }

        [JsonPropertyName("s")]
        public string? S { get; set; }

        [JsonPropertyName("sig")]
        public string? Sig { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        // Normalize alternate field names
        public string? EffectiveN => N ?? SolvedN;
        public string? EffectiveSig => Sig ?? Signature ?? S;
    }
}
