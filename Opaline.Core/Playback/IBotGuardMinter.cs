namespace Opaline.Core.Playback;

/// <summary>
/// Local BotGuard integrity / GVS pot mint (WebView2 or other host).
/// iOS moved off WKWebView to remote bgutil because GVS often rejected
/// on-device tokens; Windows still tries local first, then remote.
/// </summary>
public interface IBotGuardMinter
{
    bool IsAvailable { get; }

    /// <summary>
    /// Mint a pot bound to <paramref name="contentBinding"/> (usually video id)
    /// for the named InnerTube client (WEB / MWEB / ANDROID / TVHTML5).
    /// </summary>
    Task<string?> MintAsync(
        string contentBinding,
        string client = "WEB",
        CancellationToken ct = default);
}
