using vKOROBKU.App.Resources;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed partial class GameIdentityService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string[] IgnoredExecutableParts =
    [
        "unins", "uninstall", "setup", "crash", "report", "redist", "prereq", "unitycrash", "vc_redist",
        "bootstrap", "packagedgame"
    ];

    public async Task<DetectedGameIdentity> DetectAsync(string installPath, CancellationToken cancellationToken = default)
    {
        var gog = TryReadGogIdentity(installPath);
        if (gog is not null)
        {
            var gogSteamMatch = await TryFindSteamMatchAsync(gog.Name, cancellationToken);
            return gogSteamMatch ?? gog;
        }

        var folderCandidate = CleanName(new DirectoryInfo(installPath).Name);
        var executableCandidate = FindExecutableProductName(installPath);
        var candidate = executableCandidate ?? folderCandidate;
        var localSteamId = TryReadSteamAppId(installPath);
        if (localSteamId is not null)
        {
            var canonical = await TryGetSteamAppNameAsync(localSteamId, cancellationToken);
            return new DetectedGameIdentity(canonical ?? candidate, localSteamId, "steam_appid.txt");
        }

        var steamMatch = await TryFindSteamMatchAsync(folderCandidate, cancellationToken);
        if (steamMatch is null && executableCandidate is not null &&
            !string.Equals(executableCandidate, folderCandidate, StringComparison.OrdinalIgnoreCase))
            steamMatch = await TryFindSteamMatchAsync(executableCandidate, cancellationToken);
        return steamMatch ?? new DetectedGameIdentity(candidate, null, Strings.Identity_SourceFolderName);
    }

    public async Task<DetectedGameIdentity> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var cleaned = CleanName(name);
        return await TryFindSteamMatchAsync(cleaned, cancellationToken)
               ?? new DetectedGameIdentity(cleaned, null, Strings.Identity_SourceUser);
    }

    /// <summary>Resolves a Steam app id from a game name for cover lookup — lets non-Steam
    /// games reuse the no-auth Steam cover CDN. Returns null when there is no confident match.
    /// Network failures propagate so callers can tell a miss from an outage.</summary>
    public async Task<string?> FindSteamAppIdAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var match = await FindSteamMatchAsync(CleanName(name), cancellationToken);
        return match?.SteamAppId;
    }

    private static DetectedGameIdentity? TryReadGogIdentity(string installPath)
    {
        try
        {
            var candidates = new List<(string? GameId, string? RootGameId, string? Name)>();
            foreach (var infoPath in Directory.EnumerateFiles(installPath, "goggame-*.info", SearchOption.TopDirectoryOnly))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(infoPath));
                candidates.Add((
                    document.RootElement.TryGetProperty("gameId", out var gameId) ? gameId.ToString() : null,
                    document.RootElement.TryGetProperty("rootGameId", out var rootGameId) ? rootGameId.ToString() : null,
                    document.RootElement.TryGetProperty("name", out var name) ? name.GetString() : null));
            }
            var selected = candidates.FirstOrDefault(item =>
                               !string.IsNullOrWhiteSpace(item.GameId) && item.GameId == item.RootGameId)
                           is var root && !string.IsNullOrWhiteSpace(root.Name)
                ? root
                : candidates.FirstOrDefault();
            return string.IsNullOrWhiteSpace(selected.Name)
                ? null
                : new DetectedGameIdentity(selected.Name, null, "GOG");
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string? FindExecutableProductName(string installPath)
    {
        var candidates = new List<(string Name, long Score)>();
        foreach (var path in EnumerateExecutables(installPath))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (IgnoredExecutableParts.Any(part => fileName.Contains(part, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var version = FileVersionInfo.GetVersionInfo(path);
                var productName = version.ProductName?.Trim();
                var description = version.FileDescription?.Trim();
                var name = IsUsefulName(productName) ? productName! : IsUsefulName(description) ? description! : null;
                if (name is null)
                    continue;
                var size = new FileInfo(path).Length;
                var rootBonus = string.Equals(Path.GetDirectoryName(path), installPath, StringComparison.OrdinalIgnoreCase) ? 2_000_000_000L : 0;
                candidates.Add((CleanName(name), rootBonus + Math.Min(size, 1_500_000_000L)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return candidates.OrderByDescending(candidate => candidate.Score).Select(candidate => candidate.Name).FirstOrDefault();
    }

    private static IReadOnlyList<string> EnumerateExecutables(string rootPath)
    {
        var result = new List<string>();
        var directories = new Queue<(string Path, int Depth)>();
        directories.Enqueue((rootPath, 0));
        while (directories.TryDequeue(out var item))
        {
            try
            {
                result.AddRange(Directory.EnumerateFiles(item.Path, "*.exe", SearchOption.TopDirectoryOnly));
                if (item.Depth >= 4)
                    continue;
                foreach (var child in Directory.EnumerateDirectories(item.Path))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            directories.Enqueue((child, item.Depth + 1));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static string? TryReadSteamAppId(string installPath)
    {
        try
        {
            var file = Directory.EnumerateFiles(installPath, "steam_appid.txt", SearchOption.AllDirectories).FirstOrDefault();
            if (file is null)
                return null;
            var value = File.ReadAllText(file).Trim();
            return long.TryParse(value, out _) ? value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task<string?> TryGetSteamAppNameAsync(string appId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english&cc=US";
            using var document = JsonDocument.Parse(await Http.GetStringAsync(url, cancellationToken));
            if (!document.RootElement.TryGetProperty(appId, out var app) ||
                !app.TryGetProperty("success", out var success) || !success.GetBoolean() ||
                !app.TryGetProperty("data", out var data) || !data.TryGetProperty("name", out var name))
                return null;
            return name.GetString();
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    // Identity detection treats network problems as "no match"; the cover path uses
    // the throwing core directly to distinguish an outage from a genuine miss.
    private static async Task<DetectedGameIdentity?> TryFindSteamMatchAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            return await FindSteamMatchAsync(candidate, cancellationToken);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    private static async Task<DetectedGameIdentity?> FindSteamMatchAsync(string candidate, CancellationToken cancellationToken)
    {
        if (candidate.Length < 3)
            return null;
        try
        {
            var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(candidate)}&l=english&cc=US";
            using var document = JsonDocument.Parse(await Http.GetStringAsync(url, cancellationToken));
            if (!document.RootElement.TryGetProperty("items", out var items))
                return null;

            var normalizedCandidate = Normalize(candidate);
            var matches = items.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out _) && item.TryGetProperty("name", out _))
                .Select(item => new
                {
                    Id = item.GetProperty("id").GetInt64().ToString(),
                    Name = item.GetProperty("name").GetString() ?? string.Empty,
                    Score = Similarity(normalizedCandidate, Normalize(item.GetProperty("name").GetString() ?? string.Empty))
                            - AddOnPenalty(item.GetProperty("name").GetString() ?? string.Empty)
                })
                .OrderByDescending(item => item.Score)
                .ToArray();
            var best = matches.FirstOrDefault();
            return best is not null && best.Score >= 0.72
                ? new DetectedGameIdentity(best.Name, best.Id, Strings.Identity_SourceSteamCatalog)
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static double AddOnPenalty(string name)
    {
        string[] markers = ["soundtrack", "artbook", "demo", "upgrade", "bonus", "pack", "kit", "dlc", "season pass"];
        return markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)) ? 0.2 : 0;
    }

    private static double Similarity(string left, string right)
    {
        if (left == right)
            return 1;
        if (left.Length >= 4 && right.Contains(left, StringComparison.Ordinal) ||
            right.Length >= 4 && left.Contains(right, StringComparison.Ordinal))
            return 0.86;
        var leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightWords = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (leftWords.Count == 0 || rightWords.Count == 0)
            return 0;
        var intersection = leftWords.Intersect(rightWords).Count();
        var union = leftWords.Union(rightWords).Count();
        return intersection / (double)union;
    }

    private static bool IsUsefulName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length >= 3 &&
        !name.Equals("Unity Player", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("Unreal Engine", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("BootstrapPackagedGame", StringComparison.OrdinalIgnoreCase);

    private static string CleanName(string name) =>
        MultipleSpacesRegex().Replace(name.Replace('_', ' ').Replace('.', ' ').Trim(), " ");

    private static string Normalize(string name) =>
        NonAlphaNumericRegex().Replace(name.ToLowerInvariant(), " ").Trim();

    [GeneratedRegex("\\s+")]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial Regex NonAlphaNumericRegex();
}
