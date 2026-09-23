using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class GameActionPolicyTests
{
    [Theory]
    [InlineData(GameCompressionState.Uncompressed, null, false, GamePrimaryAction.Analyze)]
    [InlineData(GameCompressionState.Uncompressed, null, true, GamePrimaryAction.Compress)]
    [InlineData(GameCompressionState.Unknown, null, false, GamePrimaryAction.Analyze)]
    [InlineData(GameCompressionState.PartiallyCompressed, "LZX", false, GamePrimaryAction.Finish)]
    [InlineData(GameCompressionState.PartiallyCompressed, "XPRESS16K", true, GamePrimaryAction.Finish)]
    [InlineData(GameCompressionState.PartiallyCompressed, null, false, GamePrimaryAction.Analyze)]
    [InlineData(GameCompressionState.PartiallyCompressed, "NTFS", true, GamePrimaryAction.Compress)]
    [InlineData(GameCompressionState.Compressed, "LZX", false, GamePrimaryAction.Decompress)]
    [InlineData(GameCompressionState.Compressed, "LZX", true, GamePrimaryAction.Decompress)]
    public void PrimaryAction_RespectsAnalysisAndExistingCompression(GameCompressionState state,
        string? algorithm, bool freshAnalysis, GamePrimaryAction expected) =>
        Assert.Equal(expected, GameActionPolicy.Resolve(state, algorithm, freshAnalysis));
}
