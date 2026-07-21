using vKOROBKU.App.Resources;
using vKOROBKU.App.Models;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

/// <summary>Owns the compressed-games watch list: seeding from known statuses, the
/// startup decay check, and keeping entries in sync after worker operations. UI
/// concerns (summary text, card badges) stay with the caller via callbacks.</summary>
public sealed class WatchedGamesCoordinator
{
    internal static readonly TimeSpan CheckTtl = TimeSpan.FromDays(7);

    private readonly WatchedGameStore _store = new();
    private readonly FolderSizeScanner _folderSizeScanner = new();
    private readonly DirectStorageDetector _directStorageDetector = new();

    public sealed record CheckOutcome(int WatchedCount, IReadOnlyList<WatchedGame> Degraded);

    public static bool IsResumableAlgorithm(string? algorithm) =>
        algorithm is "XPRESS4K" or "XPRESS8K" or "XPRESS16K" or "LZX";

    // A full size walk runs when the stored build/version differs from the library one
    // (Steam build id and Epic AppVersionString share the same slot) or the last check
    // is stale; otherwise the recent measurement is trusted regardless of the launcher.
    internal static bool ShouldRescan(WatchedGame entry, string? libraryBuildId, DateTimeOffset now, bool force)
    {
        if (force)
            return true;
        var buildChanged = !string.IsNullOrWhiteSpace(entry.SteamBuildId) &&
                           !string.IsNullOrWhiteSpace(libraryBuildId) &&
                           !string.Equals(entry.SteamBuildId, libraryBuildId, StringComparison.Ordinal);
        return buildChanged || now - entry.LastCheckedAtUtc >= CheckTtl;
    }

    // Reflects operations that already updated the store (a recompression resets the
    // baseline, a decompression removes the entry) without a folder rescan, so the
    // summary can refresh right after a job finishes.
    public CheckOutcome ReadStoredState(UserPreferences preferences)
    {
        var watched = _store.Load();
        var degraded = watched
            .Where(entry => entry.NeedsRecompression(
                preferences.DecayThresholdPercent / 100d,
                preferences.MinimumSavingsMb * 1024L * 1024))
            .ToList();
        return new CheckOutcome(watched.Count, degraded);
    }

    public async Task<CheckOutcome> CheckAsync(
        UserPreferences preferences,
        Func<string, GameInfo?> findLibraryGame,
        Action<WatchedGame, GameInfo?> onDegraded,
        Action<WatchedGame, GameInfo?> onHealthy,
        Action<string> reportProgress,
        bool force)
    {
        var watched = _store.Load();
        if (watched.Count == 0)
            return new CheckOutcome(0, []);

        var skipSet = CompressionSkipList.BuildEffectiveSet(preferences);
        var degraded = new List<WatchedGame>();
        var position = 0;
        foreach (var entry in watched)
        {
            position++;
            var current = entry;
            if (!Directory.Exists(current.FolderPath))
            {
                try { _store.Remove(current.FolderPath); } catch { }
                AppLog.Info($"Наблюдение снято — папка не найдена: {current.FolderPath}");
                continue;
            }

            var libraryGame = findLibraryGame(current.FolderPath);
            if (ShouldRescan(current, libraryGame?.SteamBuildId, DateTimeOffset.UtcNow, force))
            {
                reportProgress(string.Format(Strings.Watcher_CheckingGame, current.DisplayName, position, watched.Count));
                var sizes = await Task.Run(() => _folderSizeScanner.Measure(current.FolderPath, skipSet));
                var hasDirectStorage = await Task.Run(() => _directStorageDetector.Detect(current.FolderPath));
                current = current with
                {
                    LastCheckedSize = sizes.PhysicalBytes,
                    LastCheckedAtUtc = DateTimeOffset.UtcNow,
                    SteamBuildId = libraryGame?.SteamBuildId ?? current.SteamBuildId,
                    HasDirectStorage = hasDirectStorage
                };
                // A check below the baseline means the baseline was captured with a
                // different methodology (before skip-aware measuring) or the game
                // shrank — re-baseline so decay starts from the current state.
                if (current.LastCheckedSize < current.LastCompressedSize)
                {
                    current = current with
                    {
                        LastCompressedSize = sizes.PhysicalBytes,
                        LastUncompressedSize = sizes.LogicalBytes
                    };
                    AppLog.Info($"Базовые размеры наблюдения пересчитаны: {current.FolderPath}");
                }
                try { _store.Upsert(current); }
                catch (Exception exception) { AppLog.Error("Не удалось сохранить watcher.json", exception); }
            }

            if (current.NeedsRecompression(
                    preferences.DecayThresholdPercent / 100d,
                    preferences.MinimumSavingsMb * 1024L * 1024))
            {
                degraded.Add(current);
                onDegraded(current, libraryGame);
            }
            else
            {
                onHealthy(current, libraryGame);
            }
        }

        return new CheckOutcome(watched.Count, degraded);
    }

    // Games compressed before the watch list existed (or by earlier versions) are
    // adopted from the saved status so monitoring works without recompressing them.
    public void SeedFromLibrary(IEnumerable<GameInfo> games, Func<string, SavedCompressionStatus?> loadSavedStatus)
    {
        try
        {
            var watched = _store.Load();
            foreach (var game in games)
            {
                if (game.CompressionState is not (GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed) ||
                    !IsResumableAlgorithm(game.CompressionAlgorithm))
                    continue;
                if (watched.Any(item => string.Equals(item.FolderPath, game.InstallPath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var saved = loadSavedStatus(game.InstallPath);
                if (saved is null || saved.PhysicalBytes <= 0 || saved.LogicalBytes <= 0)
                    continue;

                _store.Upsert(new WatchedGame(
                    game.InstallPath,
                    game.Name,
                    string.Equals(game.Source, "Steam", StringComparison.OrdinalIgnoreCase),
                    game.SteamAppId,
                    game.SteamBuildId ?? saved.SteamBuildId,
                    game.CompressionAlgorithm!,
                    saved.CheckedAt,
                    saved.PhysicalBytes,
                    saved.LogicalBytes,
                    saved.PhysicalBytes,
                    saved.CheckedAt,
                    game.HasDirectStorage ?? saved.HasDirectStorage == true));
                AppLog.Info($"Наблюдение добавлено по сохранённому статусу: {game.InstallPath}");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Не удалось пополнить список наблюдения", exception);
        }
    }

    // Hiding a game from the library also stops watching it: an invisible card must
    // not keep feeding the "needs recompression" banner. When the game is restored,
    // SeedFromLibrary re-adopts it from the kept saved status on the next check.
    public void Forget(string folderPath)
    {
        try
        {
            _store.Remove(folderPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Не удалось убрать игру из наблюдения", exception);
        }
    }

    public void OnOperationCompleted(WorkerJob job, WorkerMessage result, GameCompressionState newState, GameInfo? processedGame)
    {
        try
        {
            if (job.Operation == "compress" && newState != GameCompressionState.Uncompressed &&
                !string.IsNullOrWhiteSpace(job.Algorithm))
            {
                _store.Upsert(new WatchedGame(
                    job.RootPath,
                    processedGame?.Name ?? Path.GetFileName(job.RootPath),
                    string.Equals(processedGame?.Source, "Steam", StringComparison.OrdinalIgnoreCase),
                    processedGame?.SteamAppId,
                    processedGame?.SteamBuildId,
                    job.Algorithm,
                    DateTimeOffset.UtcNow,
                    Math.Max(0, result.PhysicalAfter - result.SkipListedPhysicalBytes),
                    Math.Max(0, result.TotalBytes - result.SkipListedBytes),
                    Math.Max(0, result.PhysicalAfter - result.SkipListedPhysicalBytes),
                    DateTimeOffset.UtcNow,
                    processedGame?.HasDirectStorage == true));
                AppLog.Info($"Наблюдение обновлено после сжатия: {job.RootPath} ({job.Algorithm})");
            }
            else if (job.Operation == "decompress" && newState == GameCompressionState.Uncompressed)
            {
                _store.Remove(job.RootPath);
                AppLog.Info($"Наблюдение снято после распаковки: {job.RootPath}");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("Не удалось обновить watcher.json", exception);
        }
    }
}
