using vKOROBKU.App.Models;

namespace vKOROBKU.Tests;

public sealed class WatchedGameTests
{
    private const long GiB = 1024L * 1024 * 1024;
    private const long MiB = 1024L * 1024;
    private const double DecayThreshold = 0.05;
    private static readonly long MinimumSavings = 500 * MiB;

    [Fact]
    public void DecayPercentage_FreshlyCompressed_IsZero()
    {
        var game = Game(compressed: 6 * GiB, uncompressed: 10 * GiB, checkedSize: 6 * GiB);

        Assert.Equal(0, game.DecayPercentage);
        Assert.Equal(0, game.PotentialSavingsBytes);
    }

    [Fact]
    public void DecayPercentage_HalfOfSavingLost_IsHalf()
    {
        var game = Game(compressed: 6 * GiB, uncompressed: 10 * GiB, checkedSize: 8 * GiB);

        Assert.Equal(0.5, game.DecayPercentage, 3);
        Assert.Equal(2 * GiB, game.PotentialSavingsBytes);
    }

    [Fact]
    public void DecayPercentage_GameGrewBeyondUncompressed_IsClampedToOne()
    {
        var game = Game(compressed: 6 * GiB, uncompressed: 10 * GiB, checkedSize: 14 * GiB);

        Assert.Equal(1, game.DecayPercentage);
    }

    [Fact]
    public void DecayPercentage_NonPositiveDenominator_IsZero()
    {
        var game = Game(compressed: 10 * GiB, uncompressed: 10 * GiB, checkedSize: 12 * GiB);

        Assert.Equal(0, game.DecayPercentage);
    }

    [Fact]
    public void NeedsRecompression_BothThresholdsExceeded_IsTrue()
    {
        var game = Game(compressed: 6 * GiB, uncompressed: 10 * GiB, checkedSize: 8 * GiB);

        Assert.True(game.NeedsRecompression(DecayThreshold, MinimumSavings));
    }

    [Fact]
    public void NeedsRecompression_SmallAbsoluteSavings_IsFalse()
    {
        var game = Game(compressed: 600 * MiB, uncompressed: 1024 * MiB, checkedSize: 900 * MiB);

        Assert.True(game.DecayPercentage > DecayThreshold);
        Assert.False(game.NeedsRecompression(DecayThreshold, MinimumSavings));
    }

    [Fact]
    public void NeedsRecompression_SmallDecayShare_IsFalse()
    {
        var game = Game(compressed: 60 * GiB, uncompressed: 100 * GiB, checkedSize: 61 * GiB);

        Assert.True(game.PotentialSavingsBytes > MinimumSavings);
        Assert.False(game.NeedsRecompression(DecayThreshold, MinimumSavings));
    }

    [Fact]
    public void NeedsRecompression_DirectStorageGame_IsAlwaysFalse()
    {
        var game = Game(compressed: 6 * GiB, uncompressed: 10 * GiB, checkedSize: 9 * GiB) with
        {
            HasDirectStorage = true
        };

        Assert.False(game.NeedsRecompression(DecayThreshold, MinimumSavings));
    }

    private static WatchedGame Game(long compressed, long uncompressed, long checkedSize) =>
        new(@"C:\Games\Demo", "Demo", true, "123", "456", "XPRESS16K",
            DateTimeOffset.UtcNow.AddDays(-7), compressed, uncompressed,
            checkedSize, DateTimeOffset.UtcNow);
}
