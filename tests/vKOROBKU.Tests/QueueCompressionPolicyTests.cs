using vKOROBKU.App.Models;
using vKOROBKU.App.Services;
using vKOROBKU.App.ViewModels;

namespace vKOROBKU.Tests;

public sealed class QueueCompressionPolicyTests
{
    private static readonly CompressionEstimate Slow = new(CompressionAlgorithm.Lzx,
        100, 50, 75, .5, AnalysisConfidence.High, 70, PerformanceImpact.PossiblySlower, 100);

    [Theory]
    [InlineData("old", "new")]
    [InlineData(null, "new")]
    [InlineData("old", null)]
    public void ChangedOrMissingBuild_DoesNotReuseOldRecommendation(string? savedBuild, string? currentBuild)
    {
        var game = new GameInfo("Game", @"C:\Games\Game", 1000, "Steam", "1", currentBuild);
        var saved = new SavedGameAnalysis(game.InstallPath, DateTimeOffset.Now,
            new GameAnalysisResult(1000, 1000, 1, 0, 100, [Slow]), savedBuild);
        var decision = QueueCompressionPolicy.Resolve(game, "auto", saved, MainViewModel.ChooseBalancedEstimate);
        Assert.Equal("XPRESS16K", decision.Algorithm);
        Assert.Equal(QueueMethodBasis.StaleAnalysis, decision.Basis);
        Assert.Null(decision.Estimate);
    }

    [Fact]
    public void CurrentAnalysis_PreservesTheSlowdownEvidenceForConfirmation()
    {
        var game = new GameInfo("Game", @"C:\Games\Game", 1000, "Steam", "1", "new");
        var saved = new SavedGameAnalysis(game.InstallPath, DateTimeOffset.Now,
            new GameAnalysisResult(1000, 1000, 1, 0, 100, [Slow]), "new");
        var decision = QueueCompressionPolicy.Resolve(game, "auto", saved, MainViewModel.ChooseBalancedEstimate);
        Assert.Equal(QueueMethodBasis.Analysis, decision.Basis);
        Assert.Equal("LZX", decision.Algorithm);
        Assert.True(QueueCompressionPolicy.NeedsSlowdownConfirmation(decision.Estimate));
    }

    [Fact]
    public void InterruptedGame_KeepsAlgorithmEvenWithoutAnalysis()
    {
        var game = new GameInfo("Game", @"C:\Games\Game", 1000, "Steam", "1")
        {
            CompressionState = GameCompressionState.PartiallyCompressed,
            CompressionAlgorithm = "XPRESS8K"
        };
        var decision = QueueCompressionPolicy.Resolve(game, "auto", null, MainViewModel.ChooseBalancedEstimate);
        Assert.Equal("XPRESS8K", decision.Algorithm);
        Assert.Equal(QueueMethodBasis.Resume, decision.Basis);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void DirectStorage_RequiresBothExpertModeAndExplicitConsent(bool detected, bool expert, bool consent, bool allowed) =>
        Assert.Equal(allowed, QueueCompressionPolicy.CanCompress(detected, expert, consent));

    [Theory]
    [InlineData(85, false)]
    [InlineData(84, true)]
    [InlineData(110, false)]
    public void SlowdownWarning_UsesMeasuredThreshold(double speed, bool warn) =>
        Assert.Equal(warn, QueueCompressionPolicy.NeedsSlowdownConfirmation(Slow with { ReadMegabytesPerSecond = speed }));
}
