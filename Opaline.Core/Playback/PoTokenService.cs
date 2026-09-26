using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Opaline.Core.Config;

namespace Opaline.Core.Playback;

/// <summary>
/// Remote GVS proof-of-origin (pot) provider — POST /get_pot.
/// Tokens are bound to a content id (usually the video id) and a client name.
/// </summary>
public sealed class PoTokenService
{
    private readonly HttpClient _http;
    private readonly BotGuardPoTokenClient _botGuard = new();
    private readonly ConcurrentDictionary<string, CachedMint> _cache = new();
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(30);

    public PoTokenService(HttpClient http) => _http = http;

    /// <summary>Wire WebView2 (or other) local minter from the App layer.</summary>
    public void AttachBotGuard(IBotGuardMinter minter) => _botGuard.Attach(minter);

    public bool HasLocalBotGuard => _botGuard.IsLocalMintAvailable;

    public async Task<string?> FetchAsync(
        string contentBinding,
        string client = "WEB",
        CancellationToken ct = default)
    {
        var key = $"{client}|{contentBinding}";
        if (_cache.TryGetValue(key, out var cached)
            && DateTimeOffset.UtcNow - cached.Minted < TokenTtl)
            return cached.Token;

        // Local BotGuard path (not available yet on Windows)
        var local = await _botGuard.TryMintLocalAsync(contentBinding, client, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(local))
        {
            _cache[key] = new CachedMint(local!, DateTimeOffset.UtcNow);
            return local;
        }

        var endpoint = AppUrls.PoTokenProvider.GetPot;
        if (endpoint is null) return null;

        try
        {
            var body = new
            {
                content_binding = contentBinding,
                client
            };
            using var resp = await _http.PostAsJsonAsync(endpoint, body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var result = await resp.Content.ReadFromJsonAsync<PotResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            var token = result?.PoToken ?? result?.Token;
            if (string.IsNullOrEmpty(token)) return null;
            _cache[key] = new CachedMint(token, DateTimeOffset.UtcNow);
            return token;
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(string contentBinding, string client = "WEB")
    {
        _cache.TryRemove($"{client}|{contentBinding}", out _);
    }

    private sealed record CachedMint(string Token, DateTimeOffset Minted);

    private sealed class PotResponse
    {
        [JsonPropertyName("poToken")]
        public string? PoToken { get; set; }

        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }
}
