using vKOROBKU.App.Models;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Services;

public enum QueueMethodBasis { Manual, Resume, Analysis, MissingAnalysis, StaleAnalysis }

public sealed record QueueMethodDecision(string Algorithm, QueueMethodBasis Basis, CompressionEstimate? Estimate = null)
{
    public string Description => Basis switch
    {
        QueueMethodBasis.Manual => Strings.Queue_BasisManual,
        QueueMethodBasis.Resume => Strings.Queue_BasisResume,
        QueueMethodBasis.Analysis => Strings.Queue_BasisAnalysis,
        QueueMethodBasis.StaleAnalysis => Strings.Queue_BasisStale,
        _ => Strings.Queue_BasisDefault
    };
}

/// <summary>Explains the queue's choice without treating an old build's benchmark as current.</summary>
public static class QueueCompressionPolicy
{
    public static QueueMethodDecision Resolve(GameInfo game, string method, SavedGameAnalysis? saved,
        Func<IReadOnlyList<CompressionEstimate>, CompressionEstimate?> choose)
    {
        if (method != "auto")
            return new(method, QueueMethodBasis.Manual);
        if (game.CompressionState == GameCompressionState.PartiallyCompressed &&
            WatchedGamesCoordinator.IsResumableAlgorithm(game.CompressionAlgorithm))
            return new(game.CompressionAlgorithm!, QueueMethodBasis.Resume);
        if (saved is null)
            return new("XPRESS16K", QueueMethodBasis.MissingAnalysis);
        if (!string.Equals(saved.SteamBuildId, game.SteamBuildId, StringComparison.Ordinal))
            return new("XPRESS16K", QueueMethodBasis.StaleAnalysis);
        var estimate = choose(saved.Result.Estimates);
        return estimate is null
            ? new("XPRESS16K", QueueMethodBasis.MissingAnalysis)
            : new(estimate.AlgorithmText, QueueMethodBasis.Analysis, estimate);
    }

    public static bool NeedsSlowdownConfirmation(CompressionEstimate? estimate) =>
        estimate is { BaselineReadMegabytesPerSecond: > 0 } &&
        estimate.ReadMegabytesPerSecond / estimate.BaselineReadMegabytesPerSecond < 0.85;

    public static bool CanCompress(bool hasDirectStorage, bool expertMode, bool explicitlyConfirmed) =>
        !hasDirectStorage || (expertMode && explicitlyConfirmed);
}
