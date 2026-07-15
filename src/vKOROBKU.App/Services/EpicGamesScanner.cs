using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class EpicGamesScanner : IGameScanner
{
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInfo>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameInfo> Scan(CancellationToken cancellationToken)
    {
        var manifestsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestsDirectory))
            return [];

        string[] files;
        try { files = Directory.GetFiles(manifestsDirectory, "*.item"); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }

        var games = new List<GameInfo>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = ParseManifest(File.ReadAllText(file));
                if (manifest is null || !Directory.Exists(manifest.InstallPath))
                    continue;
                games.Add(new GameInfo(
                    manifest.Name, GamePath.Normalize(manifest.InstallPath), manifest.SizeBytes, "Epic",
                    steamAppId: null, steamBuildId: manifest.Version));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }

        return games
            .DistinctBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // Only manifests tagged with the "games" category are real installed games; the
    // launcher also writes manifests for the engine, plugins, tools and "digitalextras"
    // (mods/DLC that share the main game's folder), which must be excluded.
    internal static EpicManifest? ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) &&
            incomplete.ValueKind == JsonValueKind.True)
            return null;
        if (!HasGamesCategory(root))
            return null;

        if (!root.TryGetProperty("DisplayName", out var name) || name.GetString() is not { Length: > 0 } displayName ||
            !root.TryGetProperty("InstallLocation", out var location) || location.GetString() is not { Length: > 0 } installPath)
            return null;

        var sizeBytes = root.TryGetProperty("InstallSize", out var installSize) &&
                        installSize.ValueKind == JsonValueKind.Number && installSize.TryGetInt64(out var bytes)
            ? bytes
            : 0;
        var version = root.TryGetProperty("AppVersionString", out var appVersion) ? appVersion.GetString() : null;

        return new EpicManifest(displayName, installPath, sizeBytes, version);
    }

    private static bool HasGamesCategory(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var category in categories.EnumerateArray())
        {
            if (category.ValueKind == JsonValueKind.String &&
                string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal sealed record EpicManifest(string Name, string InstallPath, long SizeBytes, string? Version);
}
