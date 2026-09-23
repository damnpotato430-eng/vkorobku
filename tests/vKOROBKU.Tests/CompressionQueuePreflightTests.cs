using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class CompressionQueuePreflightTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vkorobku-preflight-" + Guid.NewGuid().ToString("N"));

    public CompressionQueuePreflightTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "dstorage.dll"), "marker");
    }

    [Theory]
    [InlineData(null, false, false, QueueItemStatus.Skipped)]
    [InlineData(false, false, false, QueueItemStatus.Skipped)]
    [InlineData(false, true, false, QueueItemStatus.Skipped)]
    [InlineData(null, true, true, QueueItemStatus.Pending)]
    public async Task NewlyDetectedTitle_IsCheckedBeforeBeingAllowed(bool? cachedFlag, bool expert, bool consent, QueueItemStatus expected)
    {
        var game = new GameInfo("Game", _root, 100, "Manual") { HasDirectStorage = cachedFlag };
        var item = new CompressionQueueItem(game, "LZX");
        var confirmations = 0;
        await new CompressionQueuePreflight().PrepareAsync([item], expert, affected =>
        {
            confirmations++;
            Assert.Same(item, Assert.Single(affected));
            return consent;
        }, _ => { }, CancellationToken.None);
        Assert.True(game.HasDirectStorage);
        Assert.Equal(expected, item.Status);
        Assert.Equal(expert ? 1 : 0, confirmations);
    }

    [Fact]
    public async Task Decompression_DoesNotNeedAnException()
    {
        var item = new CompressionQueueItem(new GameInfo("Game", _root, 100, "Manual"), "", "decompress");
        await new CompressionQueuePreflight().PrepareAsync([item], false,
            _ => throw new InvalidOperationException("Should not ask"),
            _ => throw new InvalidOperationException("Should not scan"), CancellationToken.None);
        Assert.Equal(QueueItemStatus.Pending, item.Status);
    }

    [Fact]
    public async Task CancelledPreparation_DoesNotAskForConsent()
    {
        var item = new CompressionQueueItem(new GameInfo("Game", _root, 100, "Manual"), "LZX");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CompressionQueuePreflight().PrepareAsync([item], true,
                _ => throw new InvalidOperationException("Should not ask"), _ => { }, new CancellationToken(true)));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
