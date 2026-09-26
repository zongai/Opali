namespace Opaline.Core.Playback;

/// <summary>
/// AV1 ladder gate (iOS AV1Support / VTIsHardwareDecodeSupported).
/// Hardware capability is reported by the App layer via
/// <see cref="ReportHardwareProbe"/> (Windows CodecQuery); Core stays TFM-neutral.
/// </summary>
public static class Av1Support
{
    private static bool? _hardwareSupported;
    private static string _probeDetail = "not probed";

    /// <summary>User preference: allow av01 when hardware (or probe) allows.</summary>
    public static bool IsPreferred { get; set; } = true;

    /// <summary>Last hardware probe result; null until <see cref="ReportHardwareProbe"/>.</summary>
    public static bool? HardwareSupported => _hardwareSupported;

    public static string ProbeDetail => _probeDetail;

    /// <summary>
    /// Formats may enter the ladder when preferred and probe did not rule out AV1.
    /// Before probe completes, allow AV1 only if preferred (optimistic); after probe,
    /// require hardware/decoder presence like iOS.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            if (!IsPreferred) return false;
            if (_hardwareSupported is null) return true; // not probed yet
            return _hardwareSupported.Value;
        }
    }

    /// <summary>Called from App after CodecQuery (or MF) probe.</summary>
    public static void ReportHardwareProbe(bool supported, string detail)
    {
        _hardwareSupported = supported;
        _probeDetail = detail;
    }

    public static void ResetProbe()
    {
        _hardwareSupported = null;
        _probeDetail = "not probed";
    }

    public static bool AllowsMime(string? mimeOrCodecs)
    {
        if (string.IsNullOrEmpty(mimeOrCodecs)) return true;
        var m = mimeOrCodecs.ToLowerInvariant();
        if (m.Contains("av01") || m.Contains("av1"))
            return IsSupported;
        return true;
    }
}
