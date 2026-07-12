using System.Text.Json;

namespace vKOROBKU.App.Services;

public sealed class UserPreferencesStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "preferences.json");

    public bool LoadExpertMode()
    {
        try
        {
            if (!File.Exists(_path))
                return false;
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_path))?.ExpertMode ?? false;
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }

    public void SaveExpertMode(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            new Preferences(enabled),
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private sealed record Preferences(bool ExpertMode);
}
