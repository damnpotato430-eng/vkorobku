using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Sequential, cancellable background checks. Call from the UI context:
/// probes perform their I/O asynchronously and results return to that same context.</summary>
public sealed class LibraryStatusRefresher
{
    private CancellationTokenSource? _activeRead;

    public void InterruptRead() => _activeRead?.Cancel();

    public async Task RefreshAsync(
        IReadOnlyList<GameInfo> games,
        Func<GameInfo, CancellationToken, Task<SavedCompressionStatus>> read,
        Func<GameInfo, SavedCompressionStatus, Task> apply,
        Func<bool> isBusy,
        Func<GameInfo, bool> isCurrent,
        Action<GameInfo, Exception> onError,
        CancellationToken cancellationToken)
    {
        foreach (var game in games)
        {
            while (isCurrent(game))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isBusy())
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                var previousCheck = game.CompressionCheckedAt;
                using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeRead = readCancellation;
                try
                {
                    var result = await read(game, readCancellation.Token);
                    cancellationToken.ThrowIfCancellationRequested();
                    // An operation or selected-card check may have finished during
                    // the read. Never replace its newer result with our snapshot.
                    if (!isCurrent(game) || game.CompressionCheckedAt != previousCheck)
                        break;
                    if (readCancellation.IsCancellationRequested || isBusy())
                        continue;
                    await apply(game, result);
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Foreground work interrupted this game: retry when idle.
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    onError(game, exception);
                    break;
                }
                finally
                {
                    _activeRead = null;
                }
            }
        }
    }
}
