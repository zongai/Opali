using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Opaline.Core.Config;
using Opaline.Core.Storage;

namespace Opaline.Core.Auth;

/// <summary>
/// YouTube OAuth device-code flow (TV client credentials scraped from youtube.com/tv).
/// Mirrors iOS OAuthClient.
/// </summary>
public sealed class OAuthClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private static readonly string Scope =
        "http://gdata.youtube.com https://www.googleapis.com/auth/youtube-paid-content";

    public OAuthTokens? Tokens { get; private set; }
    public bool IsSignedIn => Tokens is not null;

    public OAuthClient(HttpClient http, ITokenStore store)
    {
        _http = http;
        _store = store;
        Tokens = _store.Load();
    }

    public sealed record DeviceCodeResponse(
        string DeviceCode,
        string UserCode,
        string VerificationUrl,
        int Interval,
        string ClientId,
        string ClientSecret);

    // ── Credentials ──────────────────────────────────────────────────────

    public async Task<(string ClientId, string ClientSecret)> FetchClientCredentialsAsync(
        CancellationToken ct = default)
    {
        // Scrape TV page for embedded OAuth client id / secret
        using var req = new HttpRequestMessage(HttpMethod.Get, AppUrls.YouTubeOAuth.TvLogin);
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/Version");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var clientId = Match(html, @"""client_id""\s*:\s*""([^""]+)""")
                    ?? Match(html, @"clientId['""]\s*[:=]\s*['""]([^'""]+)");
        var clientSecret = Match(html, @"""client_secret""\s*:\s*""([^""]+)""")
                        ?? Match(html, @"clientSecret['""]\s*[:=]\s*['""]([^'""]+)");

        // Well-known YouTube TV / Living Room client (public, used by many FOSS clients)
        clientId ??= "861556708454-d6dlm3lh05idd8npek18k6be8ba3oc68.apps.googleusercontent.com";
        clientSecret ??= "SboVhoG9s0rNafixCSGGKXAT";

        return (clientId, clientSecret);
    }

    // ── Device code ──────────────────────────────────────────────────────

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        var (clientId, clientSecret) = await FetchClientCredentialsAsync(ct).ConfigureAwait(false);
        var body = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope,
            ["device_id"] = Guid.NewGuid().ToString(),
            ["device_model"] = "ytlr::"
        };

        using var content = new FormUrlEncodedContent(body);
        // YouTube device endpoint accepts both JSON and form; form is more compatible
        using var jsonContent = new StringContent(
            JsonSerializer.Serialize(new
            {
                client_id = clientId,
                scope = Scope,
                device_id = Guid.NewGuid().ToString(),
                device_model = "ytlr::"
            }),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.PostAsync(AppUrls.YouTubeOAuth.DeviceCode, jsonContent, ct)
            .ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        return new DeviceCodeResponse(
            DeviceCode: root.GetProperty("device_code").GetString()!,
            UserCode: root.GetProperty("user_code").GetString()!,
            VerificationUrl: root.TryGetProperty("verification_url", out var vu)
                ? vu.GetString()! : "https://www.google.com/device",
            Interval: root.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            ClientId: clientId,
            ClientSecret: clientSecret);
    }

    // ── Poll for tokens ──────────────────────────────────────────────────

    public async Task<OAuthTokens?> PollForTokenAsync(
        DeviceCodeResponse device,
        CancellationToken ct = default)
    {
        var interval = Math.Max(device.Interval, 3);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);

            var body = new
            {
                client_id = device.ClientId,
                client_secret = device.ClientSecret,
                code = device.DeviceCode,
                grant_type = "http://oauth.net/grant_type/device/1.0"
            };
            using var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(AppUrls.YouTubeOAuth.Token, content, ct)
                .ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                var code = err.GetString();
                if (code is "authorization_pending" or "slow_down")
                {
                    if (code == "slow_down") interval += 2;
                    continue;
                }
                // access_denied / expired_token → give up
                return null;
            }

            var access = root.GetProperty("access_token").GetString()!;
            var refresh = root.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString()! : string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;

            var tokens = new OAuthTokens
            {
                AccessToken = access,
                RefreshToken = refresh,
                ClientId = device.ClientId,
                ClientSecret = device.ClientSecret,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
            };
            Tokens = tokens;
            _store.Save(tokens);
            return tokens;
        }
        return null;
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        if (Tokens is null || string.IsNullOrEmpty(Tokens.RefreshToken)) return false;

        var body = new
        {
            client_id = Tokens.ClientId,
            client_secret = Tokens.ClientSecret,
            refresh_token = Tokens.RefreshToken,
            grant_type = "refresh_token"
        };
        using var content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(AppUrls.YouTubeOAuth.Token, content, ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return false;

        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Tokens.AccessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        Tokens.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        _store.Save(Tokens);
        return true;
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        if (Tokens is null) return null;
        if (Tokens.IsExpired)
        {
            if (!await RefreshAsync(ct).ConfigureAwait(false))
                return null;
        }
        return Tokens.AccessToken;
    }

    public void SignOut()
    {
        Tokens = null;
        _store.Clear();
    }

    private static string? Match(string input, string pattern)
    {
        var m = Regex.Match(input, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }
}
