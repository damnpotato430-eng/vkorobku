using System.Text.RegularExpressions;
using Microsoft.Win32;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed partial class SteamLibraryScanner
{
    public Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInfo>>(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<GameInfo> Scan(CancellationToken cancellationToken)
    {
        var steamPath = FindSteamPath();
        if (steamPath is null)
            return [];

        var libraries = FindLibraries(steamPath);
        var games = new List<GameInfo>();

        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            IEnumerable<string> manifests;
            try { manifests = Directory.EnumerateFiles(steamApps, "appmanifest_*.acf").ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var content = File.ReadAllText(manifest);
                    var appId = ReadValue(content, "appid");
                    var buildId = ReadValue(content, "buildid");
                    var name = ReadValue(content, "name");
                    var installDirectory = ReadValue(content, "installdir");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
                        continue;

                    var sizeText = ReadValue(content, "SizeOnDisk");
                    _ = long.TryParse(sizeText, out var size);
                    var path = Path.Combine(steamApps, "common", installDirectory);
                    if (Directory.Exists(path))
                        games.Add(new GameInfo(name, path, size, "Steam", appId, buildId));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return games
            .DistinctBy(game => game.InstallPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string? FindSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath")?.ToString();
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return Path.GetFullPath(path);

        var commonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(commonPath) ? commonPath : null;
    }

    private static IReadOnlyCollection<string> FindLibraries(string steamPath)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
        var configuration = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(configuration))
            return libraries;

        try
        {
            var content = File.ReadAllText(configuration);
            foreach (Match match in PathRegex().Matches(content))
            {
                var path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path))
                    libraries.Add(Path.GetFullPath(path));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return libraries;
    }

    private static string? ReadValue(string content, string key)
    {
        var match = Regex.Match(content, $"\\\"{Regex.Escape(key)}\\\"\\s*\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();
}
