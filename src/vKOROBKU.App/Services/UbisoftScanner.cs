using Microsoft.Win32;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Finds Ubisoft Connect games through the launcher's Installs registry
/// entries. Display names come from the matching "Uplay Install" uninstall records,
/// falling back to the folder name.</summary>
public sealed class UbisoftScanner : IGameScanner
{
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInfo>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameInfo> Scan(CancellationToken cancellationToken)
    {
        using var machineKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installsKey = machineKey.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installsKey is null)
            return [];

        var games = new List<GameInfo>();
        foreach (var gameId in installsKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var gameKey = installsKey.OpenSubKey(gameId);
                var displayName = ReadUninstallDisplayName(machineKey, gameId);
                var entry = CreateEntry(gameId, gameKey?.GetValue("InstallDir") as string, displayName);
                if (entry is null || !Directory.Exists(entry.InstallPath))
                    continue;
                games.Add(new GameInfo(
                    entry.Name, GamePath.Normalize(entry.InstallPath), 0, "Ubisoft",
                    steamAppId: null, steamBuildId: null));
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

    private static string? ReadUninstallDisplayName(RegistryKey machineKey, string gameId)
    {
        try
        {
            using var uninstallKey = machineKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install " + gameId);
            return uninstallKey?.GetValue("DisplayName") as string;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    // The launcher stores InstallDir with forward slashes and a trailing slash
    // (e.g. "D:/Games/Trackmania/"); the uninstall record may be missing, in which
    // case the folder name serves as the title.
    internal static UbisoftEntry? CreateEntry(string gameId, string? installDir, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(installDir))
            return null;
        var path = installDir.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        if (path.Length == 0)
            return null;
        var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(path) : displayName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return new UbisoftEntry(gameId, name, path);
    }

    internal sealed record UbisoftEntry(string GameId, string Name, string InstallPath);
}
