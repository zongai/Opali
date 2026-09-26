using Opaline.Core.Models;

namespace Opaline.Core.Playback;

/// <summary>iOS AutoDubPreference — pick dubbed audio when original ≠ preferred language.</summary>
public static class AutoDubPreference
{
    public static bool IsEnabled { get; set; } = true;
    public static bool IgnoreAiDubs { get; set; } = true;
    /// <summary>BCP-47-ish base code e.g. "zh", "en", "ru". Null = system UI language.</summary>
    public static string? LanguageOverride { get; set; }

    public static string EffectiveLanguageCode
    {
        get
        {
            if (!string.IsNullOrEmpty(LanguageOverride))
                return BaseCode(LanguageOverride!);
            try
            {
                var tag = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return BaseCode(tag);
            }
            catch { return "en"; }
        }
    }

    public static string BaseCode(string code)
    {
        var i = code.IndexOfAny(['-', '_']);
        return (i > 0 ? code[..i] : code).ToLowerInvariant();
    }

    /// <summary>
    /// Choose audio stream: prefer matching dub language; skip .10 AI tracks when IgnoreAiDubs.
    /// </summary>
    public static StreamInfo? SelectAudio(
        IEnumerable<StreamInfo> audioStreams,
        string? originalLanguageHint = null)
    {
        var list = audioStreams.Where(a => a.IsAudioOnly || a.MimeType.Contains("audio")).ToList();
        if (list.Count == 0) return null;
        if (!IsEnabled)
            return list.OrderByDescending(a => a.AudioIsDefault).ThenByDescending(a => a.Bitrate ?? 0).FirstOrDefault();

        var want = EffectiveLanguageCode;
        var candidates = list.AsEnumerable();
        if (IgnoreAiDubs)
            candidates = candidates.Where(a => a.AudioTrackId is null || !a.AudioTrackId.Contains(".10"));

        var match = candidates
            .Where(a => TrackMatches(a, want))
            .OrderByDescending(a => a.Bitrate ?? 0)
            .FirstOrDefault();
        if (match is not null) return match;

        if (!string.IsNullOrEmpty(originalLanguageHint)
            && BaseCode(originalLanguageHint) == want)
        {
            return list.OrderByDescending(a => a.AudioIsDefault)
                .ThenByDescending(a => a.Bitrate ?? 0).FirstOrDefault();
        }

        return list.OrderByDescending(a => a.AudioIsDefault)
            .ThenByDescending(a => a.Bitrate ?? 0).FirstOrDefault();
    }

    private static bool TrackMatches(StreamInfo a, string want)
    {
        var id = (a.AudioTrackId ?? "").ToLowerInvariant();
        var name = (a.AudioTrackName ?? "").ToLowerInvariant();
        if (id.StartsWith(want + ".") || id.Contains("." + want + ".") || id.EndsWith("." + want))
            return true;
        if (name.StartsWith(want) || name.Contains($"({want})") || name.Contains($"{want}-"))
            return true;
        if (want == "zh" && (name.Contains("chinese") || name.Contains("中文") || name.Contains("国语") || name.Contains("普通话")))
            return true;
        if (want == "en" && name.Contains("english"))
            return true;
        if (want == "ja" && (name.Contains("japanese") || name.Contains("日本語")))
            return true;
        if (want == "ko" && (name.Contains("korean") || name.Contains("한국어")))
            return true;
        if (want == "ru" && (name.Contains("russian") || name.Contains("рус")))
            return true;
        return false;
    }

    /// <summary>
    /// Dub to start on, or null to keep default — iOS AutoDubPreference.autoDubTrack.
    /// </summary>
    public static AudioTrackInfo? AutoDubTrack(IReadOnlyList<AudioTrackInfo> tracks)
    {
        if (!IsEnabled || tracks.Count <= 1) return null;
        var original = tracks.FirstOrDefault(t => t.IsOriginal);
        if (original is null) return null;
        var language = EffectiveLanguageCode;
        if (original.LanguageCode == language) return null;

        var dubs = tracks.Where(t => !t.IsOriginal && t.LanguageCode == language).ToList();
        var human = dubs.FirstOrDefault(t => !t.IsAutoDubbed);
        if (human is not null) return human;
        return IgnoreAiDubs ? null : dubs.FirstOrDefault();
    }
}
