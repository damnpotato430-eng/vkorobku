using System.Text.Json;

namespace vKOROBKU.App.Services;

/// <summary>Persistent set of install paths the user hid from the library.
/// Launcher scanners keep rediscovering the folders on every refresh, so hiding
/// must survive rescans — hence a store rather than an in-memory flag.</summary>
public sealed class HiddenGamesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _sync = new();

    public HiddenGamesStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "hidden-games.json");
    }

    public IReadOnlySet<string> Load()
    {
        lock (_sync)
            return Read();
    }

    public void Add(string installPath)
    {
        lock (_sync)
        {
            var paths = Read();
            if (paths.Add(installPath))
                Write(paths);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    private HashSet<string> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path), JsonOptions) ?? [];
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private void Write(HashSet<string> paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(
            paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), JsonOptions));
        File.Move(temporary, _path, true);
    }
}
