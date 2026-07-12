using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class IgdbCredentialStore
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "igdb.json");

    public IgdbCredentials? Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;
            var settings = JsonSerializer.Deserialize<StoredCredentials>(File.ReadAllText(_settingsPath));
            if (settings is null || string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ProtectedSecret))
                return null;

            var encrypted = Convert.FromBase64String(settings.ProtectedSecret);
            var secret = Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            return new IgdbCredentials(settings.ClientId, secret);
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }

    public void Save(IgdbCredentials credentials)
    {
        if (!credentials.IsValid)
            throw new ArgumentException("Необходимо указать Client ID и Client Secret.", nameof(credentials));

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(credentials.ClientSecret), null, DataProtectionScope.CurrentUser);
        var settings = new StoredCredentials(credentials.ClientId.Trim(), Convert.ToBase64String(encrypted));
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Delete()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    private sealed record StoredCredentials(string ClientId, string ProtectedSecret);
}
