using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class ManualGameStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "manual-games.json");
    private readonly object _sync = new();

    public IReadOnlyList<ManualGameRecord> Load()
    {
        lock (_sync)
            return Read().Where(game => Directory.Exists(game.InstallPath)).ToArray();
    }

    public void Save(ManualGameRecord game)
    {
        lock (_sync)
        {
            var games = Read();
            games.RemoveAll(item => string.Equals(item.InstallPath, game.InstallPath, StringComparison.OrdinalIgnoreCase));
            games.Add(game);
            Write(games);
        }
    }

    public void Remove(string installPath)
    {
        lock (_sync)
        {
            var games = Read();
            var removed = games.RemoveAll(item =>
                string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                Write(games);
        }
    }

    private void Write(List<ManualGameRecord> games)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(games, JsonOptions));
        File.Move(temporary, _path, true);
    }

    private List<ManualGameRecord> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<List<ManualGameRecord>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }
}
