using System.Globalization;
using System.Text.RegularExpressions;

namespace Opaline.Core.Models;

public sealed class SubtitleCue
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; init; } = "";
}

public static class VttCueParser
{
    public static IReadOnlyList<SubtitleCue> Parse(string raw)
    {
        var list = new List<SubtitleCue>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        if (raw.TrimStart().StartsWith("<?xml", StringComparison.Ordinal) ||
            raw.Contains("<text ", StringComparison.Ordinal))
            return ParseXml(raw);

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.Contains("-->")) continue;
            if (!TryParseTimes(line, out var start, out var end)) continue;
            var body = new List<string>();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var b = lines[j].Trim();
                if (string.IsNullOrEmpty(b)) break;
                if (b.Contains("-->")) break;
                body.Add(StripTags(b));
            }
            if (body.Count == 0) continue;
            list.Add(new SubtitleCue { Start = start, End = end, Text = string.Join("\n", body) });
        }
        return list;
    }

    private static IReadOnlyList<SubtitleCue> ParseXml(string raw)
    {
        var list = new List<SubtitleCue>();
        var re = new Regex(
            @"<text\s+start=""([\d.]+)""(?:\s+dur=""([\d.]+)"")?[^>]*>(.*?)</text>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in re.Matches(raw))
        {
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                continue;
            double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
            var text = StripTags(System.Net.WebUtility.HtmlDecode(m.Groups[3].Value));
            if (string.IsNullOrWhiteSpace(text)) continue;
            var start = TimeSpan.FromSeconds(s);
            var end = d > 0 ? start + TimeSpan.FromSeconds(d) : start + TimeSpan.FromSeconds(3);
            list.Add(new SubtitleCue { Start = start, End = end, Text = text });
        }
        return list;
    }

    private static bool TryParseTimes(string line, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        var parts = line.Split(new[] { "-->" }, StringSplitOptions.None);
        if (parts.Length < 2) return false;
        return TryParseTs(parts[0].Trim().Split(' ')[0], out start)
            && TryParseTs(parts[1].Trim().Split(' ')[0], out end);
    }

    private static bool TryParseTs(string s, out TimeSpan ts)
    {
        ts = default;
        var bits = s.Split(':');
        try
        {
            if (bits.Length == 3)
            {
                var sec = double.Parse(bits[2], CultureInfo.InvariantCulture);
                ts = new TimeSpan(0, int.Parse(bits[0]), int.Parse(bits[1]), (int)sec, (int)((sec % 1) * 1000));
                return true;
            }
            if (bits.Length == 2)
            {
                var sec = double.Parse(bits[1], CultureInfo.InvariantCulture);
                ts = new TimeSpan(0, 0, int.Parse(bits[0]), (int)sec, (int)((sec % 1) * 1000));
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string StripTags(string s) =>
        Regex.Replace(s, @"<[^>]+>", "").Trim();
}
