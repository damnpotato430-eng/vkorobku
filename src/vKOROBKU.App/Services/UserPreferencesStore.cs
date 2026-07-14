using System.Text.Json;

namespace vKOROBKU.App.Services;

public sealed record UserPreferences(
    bool ExpertMode = false,
    bool WatcherEnabled = true,
    double DecayThresholdPercent = 5,
    int MinimumSavingsMb = 500,
    bool SkipNonCompressable = true,
    IReadOnlyList<string>? UserSkipExtensions = null);

public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "preferences.json");

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new UserPreferences();
            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), JsonOptions)
                   ?? new UserPreferences();
        }
        catch (IOException) { return new UserPreferences(); }
        catch (JsonException) { return new UserPreferences(); }
    }

    public void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(temporary, _path, true);
    }
}
