using System.Net.Http;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Downloads covers for a set of games with bounded concurrency. The first
/// transport failure stops the remaining downloads: the service is likely unreachable,
/// and every further attempt would only burn its per-title timeout.</summary>
public sealed class CoverBatchLoader(CoverService coverService)
{
    private const int MaximumConcurrency = 4;

    public sealed record BatchResult(int Completed, string? FailureMessage);

    public async Task<BatchResult> LoadAsync(
        IReadOnlyList<GameInfo> games,
        bool forceRefresh,
        Func<GameInfo, string, Task> applyCoverAsync,
        Func<int, Task> reportCompletedAsync)
    {
        using var semaphore = new SemaphoreSlim(MaximumConcurrency, MaximumConcurrency);
        using var cancellation = new CancellationTokenSource();
        var failureLock = new object();
        string? failureMessage = null;
        var completed = 0;

        void StopRemaining(string message)
        {
            lock (failureLock)
            {
                if (failureMessage is not null)
                    return;
                failureMessage = message;
                cancellation.Cancel();
            }
        }

        var tasks = games.Select(async game =>
        {
            var entered = false;
            try
            {
                await semaphore.WaitAsync(cancellation.Token);
                entered = true;
                var coverPath = await coverService.GetCoverAsync(game, forceRefresh, cancellation.Token);
                if (coverPath is not null)
                    await applyCoverAsync(game, coverPath);
            }
            catch (HttpRequestException)
            {
                StopRemaining("Сервис обложек временно недоступен");
            }
            catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
            {
                StopRemaining("Сервис обложек не ответил вовремя");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                if (entered)
                    semaphore.Release();
                var currentCompleted = Interlocked.Increment(ref completed);
                if (failureMessage is null)
                    await reportCompletedAsync(currentCompleted);
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        return new BatchResult(completed, failureMessage);
    }
}
