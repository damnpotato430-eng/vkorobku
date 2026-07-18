using Microsoft.Win32;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Finds EA app (ex-Origin) games through the per-game registry entries the
/// installers write. The EA app's own install database is encrypted and deliberately
/// not parsed — entries without an install path (leftovers of removed games) are
/// skipped.</summary>
public sealed class EaScanner : IGameScanner
{
    private static readonly string[] Hives = [@"SOFTWARE\EA Games", @"SOFTWARE\Origin Games"];

    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInfo>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameInfo> Scan(CancellationToken cancellationToken)
    {
        using var machineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        var games = new List<GameInfo>();
        foreach (var hive in Hives)
        {
            using var hiveKey = machineKey.OpenSubKey(hive);
            if (hiveKey is null)
                continue;
            foreach (var keyName in hiveKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var gameKey = hiveKey.OpenSubKey(keyName);
                    if (gameKey is null)
                        continue;
                    var entry = CreateEntry(
                        keyName,
                        gameKey.GetValue("Install Dir") as string ?? gameKey.GetValue("InstallDir") as string,
                        gameKey.GetValue("DisplayName") as string);
                    if (entry is null || !Directory.Exists(entry.InstallPath))
                        continue;
                    games.Add(new GameInfo(
                        entry.Name, GamePath.Normalize(entry.InstallPath), 0, "EA",
                        steamAppId: null, steamBuildId: null));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        return games
            .DistinctBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // A key without an install path is a leftover of an uninstalled game and carries
    // nothing worth showing. Trademark glyphs are stripped so the title matches the
    // Steam cover search better.
    internal static EaEntry? CreateEntry(string keyName, string? installDir, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(installDir))
            return null;
        var path = installDir.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        if (path.Length == 0)
            return null;
        var name = string.IsNullOrWhiteSpace(displayName) ? keyName : displayName;
        name = name.Replace("™", string.Empty).Replace("®", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return new EaEntry(name, path);
    }

    internal sealed record EaEntry(string Name, string InstallPath);
}
