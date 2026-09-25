using System.Text.RegularExpressions;

namespace Opaline.Core.Playback;

/// <summary>
/// Fetches and caches YouTube player signatureTimestamp (STS).
/// Required for some /player requests. Extracted from ytcfg "STS":NNNNN.
/// Mirrors iOS SignatureTimestampService.
/// </summary>
public sealed class SignatureTimestampService
{
    private readonly HttpClient _http;
    private readonly object _lock = new();
    private int? _cached;
    private string? _jsPath;
    private DateTimeOffset? _fetchedAt;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    private static readonly Regex StsRegex = new(
        @"""STS""\s*:\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JsUrlRegex = new(
        @"""jsUrl""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SignatureTimestampService(HttpClient http) => _http = http;

    public string? JsPath
    {
        get { lock (_lock) return _jsPath; }
    }

    public async Task<int?> GetAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cached is { } ts && _fetchedAt is { } at && DateTimeOffset.UtcNow - at < Ttl)
                return ts;
        }

        foreach (var url in new[] { "https://www.youtube.com/", "https://www.youtube.com/tv" })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var m = StsRegex.Match(html);
                if (!m.Success) continue;
                var value = int.Parse(m.Groups[1].Value);
                var js = JsUrlRegex.Match(html);
                lock (_lock)
                {
                    _cached = value;
                    _fetchedAt = DateTimeOffset.UtcNow;
                    if (js.Success) _jsPath = js.Groups[1].Value;
                }
                return value;
            }
            catch
            {
                // try next source
            }
        }
        return null;
    }
}
