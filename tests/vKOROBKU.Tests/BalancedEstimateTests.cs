using vKOROBKU.App.Models;
using vKOROBKU.App.ViewModels;

namespace vKOROBKU.Tests;

public sealed class BalancedEstimateTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private const long MiB = 1024L * 1024;

    [Fact]
    public void ChoosesLargestSavingsWhenSpeedIsAcceptable()
    {
        var lzx = Estimate(CompressionAlgorithm.Lzx, 10 * GiB, 90, 100);
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 8 * GiB, 98, 100);

        Assert.Same(lzx, MainViewModel.ChooseBalancedEstimate([lzx, xpress16]));
    }

    [Fact]
    public void RejectsAlgorithmsWithHeavyReadSlowdown()
    {
        var lzx = Estimate(CompressionAlgorithm.Lzx, 10 * GiB, 80, 100);
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 8 * GiB, 95, 100);

        Assert.Same(xpress16, MainViewModel.ChooseBalancedEstimate([lzx, xpress16]));
    }

    [Fact]
    public void PrefersFasterAlgorithmWhenSavingsAreClose()
    {
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 5 * GiB, 95, 100);
        var xpress8 = Estimate(CompressionAlgorithm.Xpress8K, 5 * GiB - 200 * MiB, 99, 100);

        Assert.Same(xpress8, MainViewModel.ChooseBalancedEstimate([xpress16, xpress8]));
    }

    [Fact]
    public void KeepsLargerSavingsWhenDifferenceIsSignificant()
    {
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 5 * GiB, 95, 100);
        var xpress8 = Estimate(CompressionAlgorithm.Xpress8K, 4 * GiB, 99, 100);

        Assert.Same(xpress16, MainViewModel.ChooseBalancedEstimate([xpress16, xpress8]));
    }

    [Fact]
    public void FallsBackToLargestSavingsWithoutSpeedMeasurements()
    {
        var lzx = Estimate(CompressionAlgorithm.Lzx, 10 * GiB, 0, 0);
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 8 * GiB, 0, 0);

        Assert.Same(lzx, MainViewModel.ChooseBalancedEstimate([lzx, xpress16]));
    }

    [Fact]
    public void AllAlgorithmsSlow_StillReturnsBestSavings()
    {
        var lzx = Estimate(CompressionAlgorithm.Lzx, 10 * GiB, 70, 100);
        var xpress16 = Estimate(CompressionAlgorithm.Xpress16K, 8 * GiB, 75, 100);

        Assert.Same(lzx, MainViewModel.ChooseBalancedEstimate([lzx, xpress16]));
    }

    [Fact]
    public void EmptyList_ReturnsNull()
    {
        Assert.Null(MainViewModel.ChooseBalancedEstimate([]));
    }

    private static CompressionEstimate Estimate(
        CompressionAlgorithm algorithm,
        long minimumSavingsBytes,
        double readMegabytesPerSecond,
        double baselineMegabytesPerSecond) =>
        new(algorithm, 0, minimumSavingsBytes, minimumSavingsBytes, 0.5, "Высокая",
            readMegabytesPerSecond, "—", baselineMegabytesPerSecond);
}
