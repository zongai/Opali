namespace Opaline.Core.Playback;

/// <summary>
/// Facade used by <see cref="PoTokenService"/>. Inject a WebView2-backed
/// <see cref="IBotGuardMinter"/> from the App layer at startup.
/// </summary>
public sealed class BotGuardPoTokenClient
{
    private IBotGuardMinter? _minter;

    public bool IsLocalMintAvailable => _minter?.IsAvailable == true;

    public void Attach(IBotGuardMinter minter) => _minter = minter;

    public Task<string?> TryMintLocalAsync(
        string contentBinding,
        string clientName,
        CancellationToken ct = default)
    {
        if (_minter is null || !_minter.IsAvailable)
            return Task.FromResult<string?>(null);
        return _minter.MintAsync(contentBinding, clientName, ct);
    }
}
