using System.Text.Json;
using Opaline.Core.Config;

namespace Opaline.Core.Services.Ryd;

public sealed record RydVotes(int Likes, int Dislikes, double Rating);

public sealed class ReturnYouTubeDislikeService
{
    private readonly HttpClient _http;
    public bool Enabled { get; set; } = true;

    public ReturnYouTubeDislikeService(HttpClient http) => _http = http;

    public async Task<RydVotes?> FetchVotesAsync(string videoId, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrEmpty(videoId)) return null;

        try
        {
            var url = $"{AppUrls.Ryd.Api}/votes?videoId={Uri.EscapeDataString(videoId)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);
            var root = doc.RootElement;
            var likes = root.TryGetProperty("likes", out var l) ? l.GetInt32() : 0;
            var dislikes = root.TryGetProperty("dislikes", out var d) ? d.GetInt32() : 0;
            var rating = root.TryGetProperty("rating", out var r) ? r.GetDouble() : 0;
            return new RydVotes(likes, dislikes, rating);
        }
        catch
        {
            return null;
        }
    }
}
