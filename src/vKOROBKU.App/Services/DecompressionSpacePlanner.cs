namespace vKOROBKU.App.Services;

/// <summary>Works out whether a batch of games can be decompressed without filling
/// the volumes they live on. The worker repeats this check authoritatively per job;
/// this one runs first so a doomed queue is never started and the user is told which
/// drive is short and by how much.</summary>
public static class DecompressionSpacePlanner
{
    /// <summary>Matches the worker's margin: a decompression must not drive a volume
    /// to zero free space, which would hurt far more than a refused operation.</summary>
    public const long MarginBytes = 1024L * 1024 * 1024;

    public sealed record DriveShortfall(string DriveRoot, long RequiredBytes, long AvailableBytes)
    {
        public long MissingBytes => Math.Max(0, RequiredBytes - AvailableBytes);
    }

    public sealed record GameGrowth(string DriveRoot, long GrowthBytes);

    /// <summary>Returns one entry per volume that cannot hold the expansion.
    /// An empty result means the batch fits.</summary>
    public static IReadOnlyList<DriveShortfall> FindShortfalls(
        IEnumerable<GameGrowth> games,
        Func<string, long?> availableSpaceProvider)
    {
        var required = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.DriveRoot) || game.GrowthBytes <= 0)
                continue;
            required.TryGetValue(game.DriveRoot, out var accumulated);
            required[game.DriveRoot] = accumulated + game.GrowthBytes;
        }

        var shortfalls = new List<DriveShortfall>();
        foreach (var (driveRoot, growth) in required)
        {
            var available = availableSpaceProvider(driveRoot);
            // An unknown volume is not treated as a failure: the worker checks again
            // with authoritative numbers right before touching the files.
            if (available is null)
                continue;
            var needed = growth + MarginBytes;
            if (available.Value < needed)
                shortfalls.Add(new DriveShortfall(driveRoot, needed, available.Value));
        }

        return shortfalls
            .OrderByDescending(shortfall => shortfall.MissingBytes)
            .ToArray();
    }

    public static long? GetAvailableSpace(string driveRoot)
    {
        try
        {
            var drive = new DriveInfo(driveRoot);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
