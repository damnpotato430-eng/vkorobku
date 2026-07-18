using vKOROBKU.App.Services;
using vKOROBKU.Shared;

namespace vKOROBKU.Tests;

public sealed class DetectorMercyTests
{
    [Fact]
    public void ResolveProblemBytes_AllIncompressible_ReturnsZero()
    {
        var files = new List<(string, long)> { ("big.vfs", 600), ("pack.arc", 300) };

        var problem = GameCompressionDetector.ResolveProblemBytes(
            files, 4096, 1000, CancellationToken.None,
            canRead: _ => true,
            isLikelyIncompressible: _ => true);

        Assert.Equal(0, problem);
    }

    [Fact]
    public void ResolveProblemBytes_CompressibleShareOverThreshold_StopsAfterVerdict()
    {
        var probes = 0;
        var files = new List<(string, long)> { ("big.bin", 500), ("other.bin", 400) };

        var problem = GameCompressionDetector.ResolveProblemBytes(
            files, 0, 1000, CancellationToken.None,
            canRead: _ => true,
            isLikelyIncompressible: _ => { probes++; return false; });

        // 500 problem bytes already exceed the 15% threshold of 1000 —
        // the second file must not be probed.
        Assert.Equal(500, problem);
        Assert.Equal(1, probes);
    }

    [Fact]
    public void ResolveProblemBytes_RemainderCannotChangeVerdict_StopsProbing()
    {
        var probes = 0;
        var files = new List<(string, long)> { ("a.bin", 1400), ("b.bin", 500) };

        var problem = GameCompressionDetector.ResolveProblemBytes(
            files, 0, 10000, CancellationToken.None,
            canRead: _ => true,
            isLikelyIncompressible: path => { probes++; return path == "a.bin"; });

        // Threshold is 1500. After the incompressible a.bin is forgiven, the
        // remaining 500 bytes cannot cross it — b.bin must not be probed.
        Assert.Equal(0, problem);
        Assert.Equal(1, probes);
    }

    [Fact]
    public void ResolveProblemBytes_SmallReadableFilesForgivenWithoutProbe()
    {
        var probes = 0;
        var files = new List<(string, long)> { ("tiny.cfg", 2048), ("tiny2.cfg", 1024) };

        var problem = GameCompressionDetector.ResolveProblemBytes(
            files, 4096, 10000, CancellationToken.None,
            canRead: _ => true,
            isLikelyIncompressible: _ => { probes++; return false; });

        Assert.Equal(0, problem);
        Assert.Equal(0, probes);
    }

    [Fact]
    public void IsLikelyIncompressible_RandomData_True_ZeroData_False()
    {
        var randomPath = Path.Combine(Path.GetTempPath(), $"vkorobku-probe-{Guid.NewGuid():N}.bin");
        var zeroPath = Path.Combine(Path.GetTempPath(), $"vkorobku-probe-{Guid.NewGuid():N}.bin");
        try
        {
            var random = new byte[64 * 1024];
            new Random(7).NextBytes(random);
            File.WriteAllBytes(randomPath, random);
            File.WriteAllBytes(zeroPath, new byte[64 * 1024]);

            Assert.True(CompressionHeuristics.IsLikelyIncompressible(randomPath, 16 * 1024));
            Assert.False(CompressionHeuristics.IsLikelyIncompressible(zeroPath, 16 * 1024));
            Assert.True(CompressionHeuristics.CanRead(randomPath));
        }
        finally
        {
            File.Delete(randomPath);
            File.Delete(zeroPath);
        }
    }
}
