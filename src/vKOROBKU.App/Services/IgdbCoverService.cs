using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class IgdbCoverService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly IgdbCredentialStore _credentialStore;
    private readonly SemaphoreSlim _apiGate = new(1, 1);
    private readonly string _cacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "Covers");
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;
    private DateTimeOffset _lastRequestAt;

    public IgdbCoverService(IgdbCredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public bool HasCredentials => _credentialStore.Load()?.IsValid == true;

    public async Task<string?> GetCoverAsync(GameInfo game, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var key = CreateCacheKey(game);
        var imagePath = Path.Combine(_cacheDirectory, $"{key}.jpg");
        var metadataPath = Path.Combine(_cacheDirectory, $"{key}.json");

        if (!forceRefresh && File.Exists(imagePath))
            return imagePath;

        var hasFreshNegativeCache = !forceRefresh && IsFreshNegativeCache(metadataPath);
        if (!hasFreshNegativeCache && !string.IsNullOrWhiteSpace(game.SteamAppId))
        {
            var steamCover = await TryDownloadSteamCoverAsync(game.SteamAppId, imagePath, metadataPath, cancellationToken);
            if (steamCover is not null)
                return steamCover;
        }

        if (hasFreshNegativeCache)
            return null;

        var credentials = _credentialStore.Load();
        if (credentials?.IsValid != true)
        {
            await WriteMetadataAsync(metadataPath, null, cancellationToken);
            return null;
        }

        await _apiGate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequestAt;
            if (elapsed < TimeSpan.FromMilliseconds(300))
                await Task.Delay(TimeSpan.FromMilliseconds(300) - elapsed, cancellationToken);

            var token = await GetAccessTokenAsync(credentials, cancellationToken);
            var imageId = await FindImageIdAsync(game.Name, credentials.ClientId, token, cancellationToken);
            _lastRequestAt = DateTimeOffset.UtcNow;
            if (imageId is null)
            {
                await File.WriteAllTextAsync(metadataPath,
                    JsonSerializer.Serialize(new CacheMetadata(DateTimeOffset.UtcNow, null)), cancellationToken);
                return null;
            }

            var imageUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big_2x/{imageId}.jpg";
            var bytes = await Http.GetByteArrayAsync(imageUrl, cancellationToken);
            await SaveImageAsync(imagePath, bytes, cancellationToken);
            await WriteMetadataAsync(metadataPath, imageId, cancellationToken);
            return imagePath;
        }
        finally
        {
            _apiGate.Release();
        }
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
            try
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 1024)
                    continue;

                await SaveImageAsync(imagePath, bytes, cancellationToken);
                await WriteMetadataAsync(metadataPath, $"steam:{appId}", cancellationToken);
                return imagePath;
            }
            catch (HttpRequestException)
            {
                // IGDB remains available as a fallback.
            }
        }

        return null;
    }

    private static async Task SaveImageAsync(string imagePath, byte[] bytes, CancellationToken cancellationToken)
    {
        var temporaryPath = imagePath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, imagePath, true);
    }

    private static Task WriteMetadataAsync(string metadataPath, string? imageId, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(new CacheMetadata(DateTimeOffset.UtcNow, imageId)), cancellationToken);

    public void ClearCache()
    {
        if (Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, true);
    }

    private async Task<string> GetAccessTokenAsync(IgdbCredentials credentials, CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return _accessToken;

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId,
            ["client_secret"] = credentials.ClientSecret,
            ["grant_type"] = "client_credentials"
        });
        using var response = await Http.PostAsync("https://id.twitch.tv/oauth2/token", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        _accessToken = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Twitch не вернул access token.");
        var expiresIn = document.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return _accessToken;
    }

    private static async Task<string?> FindImageIdAsync(
        string gameName, string clientId, string token, CancellationToken cancellationToken)
    {
        var escapedName = gameName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            $"search \"{escapedName}\"; fields name,cover.image_id; limit 5;",
            Encoding.UTF8,
            "text/plain");

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("cover", out var cover) &&
                cover.TryGetProperty("image_id", out var imageId))
                return imageId.GetString();
        }
        return null;
    }

    private static bool IsFreshNegativeCache(string metadataPath)
    {
        try
        {
            if (!File.Exists(metadataPath))
                return false;
            var metadata = JsonSerializer.Deserialize<CacheMetadata>(File.ReadAllText(metadataPath));
            return metadata?.ImageId is null && metadata?.CheckedAt > DateTimeOffset.UtcNow.AddDays(-7);
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }

    private static string CreateCacheKey(GameInfo game)
    {
        var identity = $"{game.SteamAppId}|{game.Name.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed record CacheMetadata(DateTimeOffset CheckedAt, string? ImageId);
}
