using Microsoft.Win32;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Finds GOG games through the registry entries every GOG installer writes
/// (offline installers and Galaxy alike), so no Galaxy client is required.</summary>
public sealed class GogScanner : IGameScanner
{
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInfo>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameInfo> Scan(CancellationToken cancellationToken)
    {
        // GOG writes to the 32-bit view (WOW6432Node on 64-bit Windows).
        using var machineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var gamesKey = machineKey.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (gamesKey is null)
            return [];

        var games = new List<GameInfo>();
        foreach (var productId in gamesKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var gameKey = gamesKey.OpenSubKey(productId);
                if (gameKey is null)
                    continue;
                var entry = CreateEntry(
                    productId,
                    gameKey.GetValue("gameName") as string,
                    gameKey.GetValue("path") as string,
                    gameKey.GetValue("dependsOn") as string,
                    gameKey.GetValue("buildId") as string,
                    gameKey.GetValue("ver") as string);
                if (entry is null || !Directory.Exists(entry.InstallPath))
                    continue;
                games.Add(new GameInfo(
                    entry.Name, GamePath.Normalize(entry.InstallPath), 0, "GOG",
                    steamAppId: null, steamBuildId: entry.BuildId,
                    gogProductId: entry.ProductId));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        return games
            .DistinctBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // DLC and bonus content get their own registry keys pointing at the base game's
    // folder, distinguished by a non-empty dependsOn — only base entries are games.
    internal static GogEntry? CreateEntry(
        string productId, string? name, string? path, string? dependsOn, string? buildId, string? version)
    {
        if (!string.IsNullOrWhiteSpace(dependsOn))
            return null;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return null;
        var build = string.IsNullOrWhiteSpace(buildId) ? version : buildId;
        return new GogEntry(productId, name, path, string.IsNullOrWhiteSpace(build) ? null : build);
    }

    internal sealed record GogEntry(string ProductId, string Name, string InstallPath, string? BuildId);
}
