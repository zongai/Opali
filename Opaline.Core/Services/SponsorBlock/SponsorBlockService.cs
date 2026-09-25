using System.Net.Http.Json;
using System.Text.Json;
using Opaline.Core.Config;

namespace Opaline.Core.Services.SponsorBlock;

public sealed class SponsorBlockService
{
    private readonly HttpClient _http;
    private readonly Dictionary<SbCategory, SbSkipBehavior> _behaviors = new();

    public bool Enabled { get; set; } = true;

    public SponsorBlockService(HttpClient http)
    {
        _http = http;
        foreach (SbCategory c in Enum.GetValues<SbCategory>())
            _behaviors[c] = c.DefaultBehavior();
    }

    public SbSkipBehavior GetBehavior(SbCategory category)
        => _behaviors.TryGetValue(category, out var b) ? b : category.DefaultBehavior();

    public void SetBehavior(SbCategory category, SbSkipBehavior behavior)
        => _behaviors[category] = behavior;

    public async Task<IReadOnlyList<SponsorBlockSegment>> FetchSegmentsAsync(
        string videoId,
        CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrEmpty(videoId))
            return Array.Empty<SponsorBlockSegment>();

        var cats = string.Join(",", Enum.GetValues<SbCategory>().Select(c => $"\"{c.ToApiString()}\""));
        var url =
            $"{AppUrls.SponsorBlock.Api}/api/skipSegments?videoID={Uri.EscapeDataString(videoId)}" +
            $"&categories=[{cats}]&actionTypes=[\"skip\",\"poi\",\"chapter\",\"full\"]";

        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return Array.Empty<SponsorBlockSegment>();
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            var list = new List<SponsorBlockSegment>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var cat = SbCategoryExtensions.FromApiString(
                    el.TryGetProperty("category", out var c) ? c.GetString() : null);
                if (cat is null) continue;

                var segment = el.TryGetProperty("segment", out var seg) ? seg : default;
                double start = 0, end = 0;
                if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() >= 2)
                {
                    start = seg[0].GetDouble();
                    end = seg[1].GetDouble();
                }

                list.Add(new SponsorBlockSegment
                {
                    Uuid = el.TryGetProperty("UUID", out var u) ? u.GetString() ?? Guid.NewGuid().ToString()
                         : Guid.NewGuid().ToString(),
                    Category = cat.Value,
                    StartTime = start,
                    EndTime = end,
                    ActionType = el.TryGetProperty("actionType", out var a) ? a.GetString() ?? "skip" : "skip"
                });
            }
            return list;
        }
        catch
        {
            return Array.Empty<SponsorBlockSegment>();
        }
    }

    /// <summary>
    /// Returns the segment that should be auto-skipped at the given playback position, if any.
    /// </summary>
    public SponsorBlockSegment? FindAutoSkip(
        IReadOnlyList<SponsorBlockSegment> segments,
        double positionSeconds)
    {
        foreach (var s in segments)
        {
            if (s.ActionType is not ("skip" or "full")) continue;
            if (GetBehavior(s.Category) != SbSkipBehavior.AutoSkip) continue;
            if (positionSeconds >= s.StartTime && positionSeconds < s.EndTime - 0.15)
                return s;
        }
        return null;
    }
}
