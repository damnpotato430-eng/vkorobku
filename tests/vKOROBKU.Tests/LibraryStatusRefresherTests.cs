using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class LibraryStatusRefresherTests
{
    private static GameInfo Game(string name = "Game") => new(name, @"C:\Games\" + name, 1000, "Steam");
    private static SavedCompressionStatus Status(GameInfo game) => new(game.InstallPath,
        GameCompressionState.Compressed, "LZX", DateTimeOffset.Now, 500, 500, 1000, 1);

    [Fact]
    public async Task UpdatesAllGamesWithoutSelection_AndContinuesAfterUnreadableGame()
    {
        var games = new[] { Game("First"), Game("Unavailable"), Game("Last") };
        var applied = new List<string>();
        var errors = new List<string>();
        await new LibraryStatusRefresher().RefreshAsync(games,
            (game, _) => game == games[1] ? Task.FromException<SavedCompressionStatus>(new IOException()) : Task.FromResult(Status(game)),
            (game, _) => { applied.Add(game.Name); return Task.CompletedTask; },
            () => false, _ => true, (game, _) => errors.Add(game.Name), CancellationToken.None);
        Assert.Equal(new[] { "First", "Last" }, applied);
        Assert.Equal(new[] { "Unavailable" }, errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DoesNotOverwriteNewerCheckOrReplacedCard(bool newerCheck)
    {
        var game = Game();
        var current = true;
        var applied = false;
        await new LibraryStatusRefresher().RefreshAsync([game],
            (item, _) =>
            {
                if (newerCheck) item.CompressionCheckedAt = DateTimeOffset.Now;
                else current = false;
                return Task.FromResult(Status(item));
            },
            (_, _) => { applied = true; return Task.CompletedTask; },
            () => false, _ => current, (_, error) => throw error, CancellationToken.None);
        Assert.False(applied);
    }

    [Fact]
    public async Task InterruptedReadIsRetried_InsteadOfApplyingItsSnapshot()
    {
        var refresher = new LibraryStatusRefresher();
        var reads = 0;
        var applied = 0;
        await refresher.RefreshAsync([Game()],
            (game, _) =>
            {
                if (++reads == 1) refresher.InterruptRead();
                return Task.FromResult(Status(game));
            },
            (_, _) => { applied++; return Task.CompletedTask; },
            () => false, _ => true, (_, error) => throw error, CancellationToken.None);
        Assert.Equal(2, reads);
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task ForegroundWorkPausesReads_AndCancellationStopsWaiting()
    {
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        var run = new LibraryStatusRefresher().RefreshAsync([Game()],
            (game, _) => { reads++; return Task.FromResult(Status(game)); },
            (_, _) => Task.CompletedTask, () => true, _ => true,
            (_, error) => throw error, cancellation.Token);
        Assert.Equal(0, reads);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task CancelledGenerationCannotApplyEvenIfProbeIgnoresCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var applied = false;
        var run = new LibraryStatusRefresher().RefreshAsync([Game()],
            (game, _) => { cancellation.Cancel(); return Task.FromResult(Status(game)); },
            (_, _) => { applied = true; return Task.CompletedTask; },
            () => false, _ => true, (_, error) => throw error, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(applied);
    }

    [Fact]
    public void UpdatedBuild_RemainsPartialUntilRecompression()
    {
        var game = new GameInfo("Game", @"C:\Games\Game", 1000, "Steam", "1", "new");
        var old = Status(game) with { SteamBuildId = "old" };
        var result = CompressionStatusRefreshPolicy.Create(game, old,
            new GameCompressionDetection(GameCompressionState.Compressed, "LZX", 400, 700, 1100, 10), false, DateTimeOffset.Now);
        Assert.Equal(GameCompressionState.PartiallyCompressed, result.State);
        Assert.Equal("old", result.SteamBuildId);
        Assert.Equal(700, result.PhysicalBytes);
        Assert.Equal(1100, result.LogicalBytes);
    }

    [Fact]
    public void LateBackgroundSaveCannotOverwriteNewerOperation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vkorobku-status-{Guid.NewGuid():N}.json");
        try
        {
            var store = new CompressionStatusStore(path);
            var old = Status(Game());
            var newer = old with { CheckedAt = old.CheckedAt.AddMinutes(1), Algorithm = "XPRESS8K" };
            store.Save(newer);
            store.Save(old);
            Assert.Equal(newer, store.Load(old.InstallPath));
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }
}
