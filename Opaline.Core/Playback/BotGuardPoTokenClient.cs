namespace Opaline.Core.Playback;

/// <summary>
/// Local BotGuard PO mint is not fully portable to WinUI without embedding
/// YouTube's challenge JS (WebView2 + integrity token). iOS uses a dedicated
/// challenge pipeline; Windows uses <see cref="PoTokenService"/> remote
/// <c>/get_pot</c> (same class of approach as many third-party clients).
///
/// This type is the extension point for a future WebView2-based mint.
/// </summary>
public sealed class BotGuardPoTokenClient
{
    public bool IsLocalMintAvailable => false;

    public Task<string?> TryMintLocalAsync(
        string contentBinding,
        string clientName,
        CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
