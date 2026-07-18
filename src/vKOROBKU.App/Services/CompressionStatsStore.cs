using System.Text.Json;

namespace vKOROBKU.App.Services;

public sealed record DriveAllTimeStats(long FreedBytes, int Operations);

public sealed record CompressionAllTimeStats(
    long FreedBytes,
    int Operations,
    DateTimeOffset? FirstOperationAt,
    DateTimeOffset? LastOperationAt,
    Dictionary<string, DriveAllTimeStats> Drives)
{
    public static CompressionAllTimeStats Empty { get; } = new(0, 0, null, null, []);
}

/// <summary>All-time compression totals, additive only: they record what the app has
/// freed over its lifetime, so a later decompression or uninstall does not rewrite
/// history (the drives panel already shows the current saving separately).</summary>
public sealed class CompressionStatsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _sync = new();

    public CompressionStatsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "stats.json");
    }

    public CompressionAllTimeStats Load()
    {
        lock (_sync)
        {
            return Read();
        }
    }

    public CompressionAllTimeStats RecordCompression(string driveRoot, long freedBytes)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(driveRoot) ? "?" : driveRoot.ToUpperInvariant();
        var freed = Math.Max(0, freedBytes);
        lock (_sync)
        {
            var stats = Read();
            var drives = new Dictionary<string, DriveAllTimeStats>(stats.Drives, StringComparer.OrdinalIgnoreCase);
            drives.TryGetValue(normalizedRoot, out var drive);
            drives[normalizedRoot] = new DriveAllTimeStats(
                (drive?.FreedBytes ?? 0) + freed,
                (drive?.Operations ?? 0) + 1);
            var now = DateTimeOffset.Now;
            var updated = stats with
            {
                FreedBytes = stats.FreedBytes + freed,
                Operations = stats.Operations + 1,
                FirstOperationAt = stats.FirstOperationAt ?? now,
                LastOperationAt = now,
                Drives = drives
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(updated, JsonOptions));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return updated;
        }
    }

    private CompressionAllTimeStats Read()
    {
        try
        {
            if (!File.Exists(_path))
                return CompressionAllTimeStats.Empty;
            return JsonSerializer.Deserialize<CompressionAllTimeStats>(File.ReadAllText(_path), JsonOptions)
                   ?? CompressionAllTimeStats.Empty;
        }
        catch (IOException) { return CompressionAllTimeStats.Empty; }
        catch (JsonException) { return CompressionAllTimeStats.Empty; }
        catch (UnauthorizedAccessException) { return CompressionAllTimeStats.Empty; }
    }
}
