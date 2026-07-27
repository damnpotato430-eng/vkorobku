using System.Text.Json;
using System.Text.Json.Serialization;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class AnalysisCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "analysis-cache.json");
    private readonly object _sync = new();

    public SavedGameAnalysis? Load(string installPath)
    {
        lock (_sync)
        {
            var cache = ReadCache();
            return cache.Analyses.FirstOrDefault(item =>
                string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(SavedGameAnalysis analysis)
    {
        lock (_sync)
        {
            var cache = ReadCache();
            cache.Analyses.RemoveAll(item =>
                string.Equals(item.InstallPath, analysis.InstallPath, StringComparison.OrdinalIgnoreCase));
            cache.Analyses.Add(analysis);
            WriteCache(cache);
        }
    }

    public void Remove(string installPath)
    {
        lock (_sync)
        {
            var cache = ReadCache();
            var removed = cache.Analyses.RemoveAll(item =>
                string.Equals(item.InstallPath, installPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                WriteCache(cache);
        }
    }

    private AnalysisCache ReadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return new AnalysisCache();
            var cache = JsonSerializer.Deserialize<AnalysisCache>(File.ReadAllText(_cachePath), JsonOptions);
            // Version 1 stored confidence and performance as already-translated text.
            // Such entries cannot be re-rendered in another language, so they are
            // dropped — the user simply re-runs the analysis.
            if (cache is null || cache.Version != AnalysisCache.CurrentVersion)
                return new AnalysisCache();
            return cache;
        }
        catch (IOException) { return new AnalysisCache(); }
        catch (JsonException) { return new AnalysisCache(); }
    }

    private void WriteCache(AnalysisCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var temporaryPath = _cachePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cache, JsonOptions));
        File.Move(temporaryPath, _cachePath, true);
    }

    private sealed class AnalysisCache
    {
        public const int CurrentVersion = 2;

        public int Version { get; init; } = CurrentVersion;
        public List<SavedGameAnalysis> Analyses { get; init; } = [];
    }
}
