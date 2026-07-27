using vKOROBKU.App.Services;
using static vKOROBKU.App.Services.DecompressionSpacePlanner;

namespace vKOROBKU.Tests;

/// <summary>Decompression expands files back to their logical size, so a batch that
/// does not fit must be refused before anything is touched — a volume driven to zero
/// free space costs the user far more than a rejected operation.</summary>
public sealed class DecompressionSpacePlannerTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void GamesOnTheSameDrive_AccumulateIntoOneRequirement()
    {
        // Two games needing 3 GB each fit in 10 GB alone, but not together with the margin.
        var shortfalls = FindShortfalls(
            [new GameGrowth(@"D:\", 3 * Gigabyte), new GameGrowth(@"d:\", 3 * Gigabyte)],
            _ => 6 * Gigabyte);

        var shortfall = Assert.Single(shortfalls);
        Assert.Equal(@"D:\", shortfall.DriveRoot);
        Assert.Equal(6 * Gigabyte + MarginBytes, shortfall.RequiredBytes);
        Assert.Equal(MarginBytes, shortfall.MissingBytes);
    }

    [Fact]
    public void EachDrive_IsJudgedOnItsOwnFreeSpace()
    {
        var shortfalls = FindShortfalls(
            [new GameGrowth(@"C:\", 2 * Gigabyte), new GameGrowth(@"E:\", 50 * Gigabyte)],
            root => root.StartsWith('C') ? 100 * Gigabyte : 10 * Gigabyte);

        var shortfall = Assert.Single(shortfalls);
        Assert.Equal(@"E:\", shortfall.DriveRoot);
    }

    [Fact]
    public void EnoughSpaceIncludingTheMargin_ReportsNothing()
    {
        Assert.Empty(FindShortfalls(
            [new GameGrowth(@"D:\", 5 * Gigabyte)],
            _ => 5 * Gigabyte + MarginBytes));
    }

    [Fact]
    public void MarginIsRequiredOnTopOfTheGrowth()
    {
        Assert.NotEmpty(FindShortfalls(
            [new GameGrowth(@"D:\", 5 * Gigabyte)],
            _ => 5 * Gigabyte + MarginBytes - 1));
    }

    [Fact]
    public void AlreadyUncompressedGames_NeedNoSpace()
    {
        Assert.Empty(FindShortfalls(
            [new GameGrowth(@"D:\", 0), new GameGrowth(@"D:\", -100)],
            _ => 0));
    }

    // The worker re-checks with authoritative numbers, so an unreadable volume must
    // not block the queue here.
    [Fact]
    public void UnknownDrive_IsLeftToTheWorker()
    {
        Assert.Empty(FindShortfalls([new GameGrowth(@"Z:\", 100 * Gigabyte)], _ => null));
    }

    [Fact]
    public void WorstShortfall_ComesFirst()
    {
        var shortfalls = FindShortfalls(
            [new GameGrowth(@"C:\", 2 * Gigabyte), new GameGrowth(@"E:\", 90 * Gigabyte)],
            _ => Gigabyte);

        Assert.Equal(@"E:\", shortfalls[0].DriveRoot);
        Assert.Equal(@"C:\", shortfalls[1].DriveRoot);
    }
}
