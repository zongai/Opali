namespace Opaline.Core.Playback;

/// <summary>
/// Compatibility shim — prefer <see cref="SignatureSolverService"/>.
/// </summary>
public sealed class NSolverService
{
    private readonly SignatureSolverService _solver;

    public NSolverService(SignatureSolverService solver) => _solver = solver;

    public Task<string?> SolveAsync(string nParam, string? playerJsPath = null, CancellationToken ct = default)
        => _solver.SolveNAsync(nParam, ct);
}
