using System.Text.Json;
using System.Text.Json.Serialization;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class CompressionStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "compression-status.json");
    private readonly object _sync = new();

    public SavedCompressionStatus? Load(string installPath)
    {
        lock (_sync)
            return Read().FirstOrDefault(item => string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(SavedCompressionStatus status)
    {
        lock (_sync)
        {
            var items = Read();
            items.RemoveAll(item => string.Equals(item.InstallPath, status.InstallPath, StringComparison.OrdinalIgnoreCase));
            items.Add(status);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(temporary, _path, true);
        }
    }

    public void Remove(string installPath)
    {
        lock (_sync)
        {
            var items = Read();
            var removed = items.RemoveAll(item =>
                string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(temporary, _path, true);
        }
    }

    private List<SavedCompressionStatus> Read()
    {
        try
        {
            if (!File.Exists(_path))
                return [];
            return JsonSerializer.Deserialize<List<SavedCompressionStatus>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }
}
