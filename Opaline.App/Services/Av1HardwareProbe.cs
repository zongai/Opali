using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Opaline.Core.Playback;
using Opaline.Core.Services;

namespace Opaline.App.Services;

/// <summary>
/// Windows AV1 hardware/decoder probe via <see cref="CodecQuery"/> —
/// counterpart to iOS VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1).
/// </summary>
public static class Av1HardwareProbe
{
    public static async Task ProbeAsync()
    {
        try
        {
            var query = new CodecQuery();
            // Empty subtype string returns all; filter AV01 / av01
            var all = await query.FindAllAsync(CodecKind.Video, CodecCategory.Decoder, "");
            var av1 = all.Where(c =>
                    c.Subtypes.Any(s =>
                        s.Contains("AV01", StringComparison.OrdinalIgnoreCase)
                        || s.Contains("AV1", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (av1.Count == 0)
            {
                // Explicit subtype query (some builds only match this)
                var direct = await query.FindAllAsync(CodecKind.Video, CodecCategory.Decoder, "AV01");
                av1 = direct.ToList();
            }

            var names = av1.Select(c => c.DisplayName).Distinct().Take(5).ToList();
            var hasAny = av1.Count > 0;

            // Prefer hardware: DisplayName often contains "Hardware" / DXVA / MFT
            var hasHw = av1.Any(c =>
            {
                var n = c.DisplayName ?? "";
                return n.Contains("Hardware", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("DXVA", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("D3D", StringComparison.OrdinalIgnoreCase)
                       || n.Contains("GPU", StringComparison.OrdinalIgnoreCase);
            });

            // iOS: hardware only. On Windows, store presence of any AV1 decoder as
            // supported, but label hardware vs software in detail.
            var supported = hasAny;
            var detail = !hasAny
                ? "no AV1 video decoder (install AV1 Video Extension?)"
                : hasHw
                    ? $"hardware AV1 decoder: {string.Join(", ", names)}"
                    : $"AV1 decoder (software/unknown): {string.Join(", ", names)}";

            Av1Support.ReportHardwareProbe(supported, detail);
            AppLog.Info("AV1", detail);
        }
        catch (Exception ex)
        {
            Av1Support.ReportHardwareProbe(false, "probe failed: " + ex.Message);
            AppLog.Error("AV1", "CodecQuery probe failed", ex);
        }
    }
}
