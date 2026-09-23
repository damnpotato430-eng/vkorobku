using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Checks every compression target before elevation. Decompression never
/// requires a DirectStorage exception. No file-changing work happens here.</summary>
public sealed class CompressionQueuePreflight
{
    private readonly DirectStorageDetector _detector = new();

    public async Task PrepareAsync(IReadOnlyList<CompressionQueueItem> items, bool expertMode,
        Func<IReadOnlyList<CompressionQueueItem>, bool> confirmDirectStorage,
        Action<GameInfo> reportChecking, CancellationToken cancellationToken)
    {
        foreach (var item in items.Where(item => !item.IsDecompression))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportChecking(item.Game);
            item.Game.HasDirectStorage = await Task.Run(
                () => _detector.Detect(item.Game.InstallPath, cancellationToken), cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var detected = items.Where(item => !item.IsDecompression && item.Game.HasDirectStorage == true).ToArray();
        var confirmed = detected.Length > 0 && expertMode && confirmDirectStorage(detected);
        foreach (var item in detected)
            if (!QueueCompressionPolicy.CanCompress(true, expertMode, confirmed))
                item.MarkSkipped("DirectStorage");
    }
}
