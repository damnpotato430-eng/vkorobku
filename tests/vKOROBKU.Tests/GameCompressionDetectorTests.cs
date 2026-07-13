using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class GameCompressionDetectorTests
{
    [Theory]
    [InlineData(0, 0, GameCompressionState.Uncompressed)]
    [InlineData(0, 1000, GameCompressionState.Uncompressed)]
    [InlineData(49, 1000, GameCompressionState.Uncompressed)]
    [InlineData(50, 1000, GameCompressionState.PartiallyCompressed)]
    [InlineData(500, 1000, GameCompressionState.PartiallyCompressed)]
    [InlineData(849, 1000, GameCompressionState.PartiallyCompressed)]
    [InlineData(850, 1000, GameCompressionState.Compressed)]
    [InlineData(1000, 1000, GameCompressionState.Compressed)]
    public void ClassifyState_UsesShareOfCompressedBytes(
        long compressedLogicalBytes,
        long totalLogicalBytes,
        GameCompressionState expected)
    {
        Assert.Equal(expected, GameCompressionDetector.ClassifyState(compressedLogicalBytes, totalLogicalBytes));
    }
}
