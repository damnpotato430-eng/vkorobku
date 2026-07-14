using System.Net.Http;
using System.Text.Json;

namespace vKOROBKU.App.Services;

/// <summary>Checks GitHub for a newer release; never installs anything.</summary>
public sealed class UpdateCheckService
{
    private const string ReleasesUrl = "https://api.github.com/repos/damnpotato430-eng/vkorobku/releases?per_page=1";
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("vKOROBKU (+https://github.com/damnpotato430-eng/vkorobku)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<(Version Version, string Url)?> CheckForNewerReleaseAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(ReleasesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out var tag) ||
                    !release.TryGetProperty("html_url", out var url) ||
                    !TryParseTag(tag.GetString(), out var version) ||
                    url.GetString() is not { } releaseUrl)
                    continue;
                return version > currentVersion ? (version, releaseUrl) : null;
            }
            return null;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0);
        var text = tag?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!Version.TryParse(text, out var parsed))
            return false;
        version = parsed;
        return true;
    }
}
