using System.Text.Json;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class WatchedGameStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "watcher.json");
    private readonly object _sync = new();

    public IReadOnlyList<WatchedGame> Load()
    {
        lock (_sync)
            return Read();
    }

    public void Upsert(WatchedGame game)
    {
        lock (_sync)
        {
            var games = Read();
            games.RemoveAll(item => string.Equals(item.FolderPath, game.FolderPath, StringComparison.OrdinalIgnoreCase));
            games.Add(game);
            Write(games);
        }
    }

    public void Remove(string folderPath)
    {
        lock (_sync)
        {
            var games = Read();
            var removed = games.RemoveAll(item =>
                string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                Write(games);
        }
    }

    private List<WatchedGame> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<List<WatchedGame>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }

    private void Write(List<WatchedGame> games)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(games, JsonOptions));
        File.Move(temporary, _path, true);
    }
}
