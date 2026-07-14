using vKOROBKU.App.Models;
using vKOROBKU.App.ViewModels;

namespace vKOROBKU.Tests;

public sealed class CompressionStatusFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnknownState_IsNeverFresh()
    {
        var game = CreateGame(GameCompressionState.Unknown, Now.AddMinutes(-1));

        Assert.False(MainViewModel.HasFreshCompressionStatus(game, Now));
    }

    [Fact]
    public void MissingCheckTime_IsNotFresh()
    {
        var game = CreateGame(GameCompressionState.Compressed, null);

        Assert.False(MainViewModel.HasFreshCompressionStatus(game, Now));
    }

    [Fact]
    public void RecentCheck_IsFresh()
    {
        var game = CreateGame(GameCompressionState.Compressed, Now.AddHours(-1));

        Assert.True(MainViewModel.HasFreshCompressionStatus(game, Now));
    }

    [Fact]
    public void ExpiredCheck_IsNotFresh()
    {
        var game = CreateGame(GameCompressionState.Uncompressed, Now - MainViewModel.CompressionStatusTtl);

        Assert.False(MainViewModel.HasFreshCompressionStatus(game, Now));
    }

    private static GameInfo CreateGame(GameCompressionState state, DateTimeOffset? checkedAt) =>
        new("Demo", @"C:\Games\Demo", 1024, "Steam", compressionState: state)
        {
            CompressionCheckedAt = checkedAt
        };
}
