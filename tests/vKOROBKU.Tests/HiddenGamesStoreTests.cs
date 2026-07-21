using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class HiddenGamesStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"vkorobku-hidden-{Guid.NewGuid():N}.json");

    [Fact]
    public void Add_PersistsAcrossInstances_AndIgnoresCase()
    {
        var store = new HiddenGamesStore(_path);
        store.Add(@"D:\Games\Forspoken");
        store.Add(@"d:\games\FORSPOKEN");
        store.Add(@"D:\Games\Ratchet");

        var reloaded = new HiddenGamesStore(_path).Load();
        Assert.Equal(2, reloaded.Count);
        // IReadOnlySet.Contains honours the store's case-insensitive comparer
        // (xunit's Assert.Contains on IEnumerable would compare ordinally).
        Assert.True(reloaded.Contains(@"d:\games\forspoken"));
        Assert.True(reloaded.Contains(@"D:\Games\Ratchet"));
    }

    [Fact]
    public void Clear_EmptiesTheStore()
    {
        var store = new HiddenGamesStore(_path);
        store.Add(@"D:\Games\Forspoken");
        store.Clear();
        Assert.Empty(store.Load());
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void CorruptedFile_ReadsAsEmpty()
    {
        File.WriteAllText(_path, "{ not json ]");
        Assert.Empty(new HiddenGamesStore(_path).Load());
    }

    public void Dispose()
    {
        try { File.Delete(_path); }
        catch (IOException) { }
    }
}
