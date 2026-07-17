using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>Resolves game cover art with no user configuration: the Steam cover CDN by
/// app id, the public GOG API by product id, and a Steam store search by name for the
/// rest. A missing cover is cached negatively for a week so it is not retried on every
/// launch.</summary>
public sealed class CoverService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly Func<string, CancellationToken, Task<string?>>? _steamAppIdResolver;
    private readonly string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "Covers");

    public CoverService(Func<string, CancellationToken, Task<string?>>? steamAppIdResolver = null)
    {
        _steamAppIdResolver = steamAppIdResolver;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("vKOROBKU/0.1 (+https://github.com/damnpotato430-eng/vkorobku)");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return client;
    }

    public async Task<string?> GetCoverAsync(GameInfo game, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var key = CreateCacheKey(game);
        var imagePath = Path.Combine(_cacheDirectory, $"{key}.jpg");
        var metadataPath = Path.Combine(_cacheDirectory, $"{key}.json");

        if (!forceRefresh && File.Exists(imagePath))
            return imagePath;
        if (!forceRefresh && IsFreshNegativeCache(metadataPath))
            return null;

        if (!string.IsNullOrWhiteSpace(game.SteamAppId))
        {
            var steamCover = await TryDownloadSteamCoverAsync(game.SteamAppId, imagePath, metadataPath, cancellationToken);
            if (steamCover is not null)
                return steamCover;
        }
        else if (!string.IsNullOrWhiteSpace(game.GogProductId))
        {
            // GOG box art comes from GOG's public catalog API — no account needed.
            var gogCover = await TryDownloadGogCoverAsync(game.GogProductId, imagePath, metadataPath, cancellationToken);
            if (gogCover is not null)
                return gogCover;
        }

        if (string.IsNullOrWhiteSpace(game.SteamAppId) && _steamAppIdResolver is not null)
        {
            // Games without an app id (Epic, manual, GOG titles missing from the GOG
            // catalog) resolve one by name so the Steam cover CDN can serve them.
            var resolvedAppId = await _steamAppIdResolver(game.Name, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolvedAppId))
            {
                var steamCover = await TryDownloadSteamCoverAsync(resolvedAppId, imagePath, metadataPath, cancellationToken);
                if (steamCover is not null)
                    return steamCover;
            }
        }

        // Only a confirmed miss (services reachable, no cover found) is cached for a
        // week. Network failures throw out of the attempts above and are not cached,
        // so covers are retried on the next launch instead of going dark for 7 days.
        await WriteMetadataAsync(metadataPath, null, cancellationToken);
        return null;
    }

    private static async Task<string?> TryDownloadSteamCoverAsync(
        string appId,
        string imagePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        var urls = new[]
        {
            $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg"
        };

        foreach (var url in urls)
        {
            var downloaded = await TryDownloadImageAsync(url, imagePath, cancellationToken);
            if (!downloaded)
                continue;
            await WriteMetadataAsync(metadataPath, $"steam:{appId}", cancellationToken);
            return imagePath;
        }

        var fallbackUrl = await TryGetSteamFallbackImageUrlAsync(appId, cancellationToken);
        if (fallbackUrl is not null && await TryDownloadImageAsync(fallbackUrl, imagePath, cancellationToken))
        {
            await WriteMetadataAsync(metadataPath, $"steam-header:{appId}", cancellationToken);
            return imagePath;
        }

        return null;
    }

    private static async Task<string?> TryDownloadGogCoverAsync(
        string productId,
        string imagePath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        string? boxArtUrl;
        try
        {
            using var response = await Http.GetAsync($"https://api.gog.com/v2/games/{productId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("_links", out var links) ||
                !links.TryGetProperty("boxArtImage", out var boxArt) ||
                !boxArt.TryGetProperty("href", out var href))
                return null;
            boxArtUrl = href.GetString();
        }
        catch (JsonException) { return null; }

        if (string.IsNullOrWhiteSpace(boxArtUrl))
            return null;
        if (!await TryDownloadImageAsync(boxArtUrl, imagePath, cancellationToken))
            return null;
        await WriteMetadataAsync(metadataPath, $"gog:{productId}", cancellationToken);
        return imagePath;
    }

    // Transport errors deliberately propagate: a 404 is a miss, but an unreachable
    // network must not be recorded as "this game has no cover".
    private static async Task<bool> TryDownloadImageAsync(
        string url,
        string imagePath,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            return false;
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length < 1024)
            return false;
        await SaveImageAsync(imagePath, bytes, cancellationToken);
        return true;
    }

    private static async Task<string?> TryGetSteamFallbackImageUrlAsync(string appId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english&cc=US";
            using var document = JsonDocument.Parse(await Http.GetStringAsync(url, cancellationToken));
            if (!document.RootElement.TryGetProperty(appId, out var app) ||
                !app.TryGetProperty("success", out var success) || !success.GetBoolean() ||
                !app.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("header_image", out var headerImage))
                return null;
            return headerImage.GetString();
        }
        catch (JsonException) { return null; }
    }

    private static async Task SaveImageAsync(string imagePath, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = imagePath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, imagePath, true);
    }

    private static Task WriteMetadataAsync(string metadataPath, string? imageId, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(new CacheMetadata(DateTimeOffset.UtcNow, imageId, 2)), cancellationToken);

    private static bool IsFreshNegativeCache(string metadataPath)
    {
        try
        {
            if (!File.Exists(metadataPath))
                return false;
            var metadata = JsonSerializer.Deserialize<CacheMetadata>(File.ReadAllText(metadataPath));
            return metadata?.Version >= 2 && metadata.ImageId is null &&
                   metadata.CheckedAt > DateTimeOffset.UtcNow.AddDays(-7);
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }

    private static string CreateCacheKey(GameInfo game)
    {
        var identity = $"{game.SteamAppId}|{game.Name.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed record CacheMetadata(DateTimeOffset CheckedAt, string? ImageId, int Version);
}
