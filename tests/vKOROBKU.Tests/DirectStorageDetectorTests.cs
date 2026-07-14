using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class DirectStorageDetectorTests
{
    [Fact]
    public void Detect_MarkerInRoot_ReturnsTrue()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "dstorage.dll"), [1]);

            Assert.True(new DirectStorageDetector().Detect(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_MarkerInNestedBinariesFolder_ReturnsTrue()
    {
        var root = CreateTempDirectory();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "Engine", "Binaries", "Win64")).FullName;
            File.WriteAllBytes(Path.Combine(nested, "dstoragecore.dll"), [1]);

            Assert.True(new DirectStorageDetector().Detect(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_NoMarkers_ReturnsFalse()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "game.exe"), [1]);
            Directory.CreateDirectory(Path.Combine(root, "data"));

            Assert.False(new DirectStorageDetector().Detect(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Detect_MarkerDeeperThanScanLimit_ReturnsFalse()
    {
        var root = CreateTempDirectory();
        try
        {
            var deep = Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "d", "e")).FullName;
            File.WriteAllBytes(Path.Combine(deep, "dstorage.dll"), [1]);

            Assert.False(new DirectStorageDetector().Detect(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vkorobku-ds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
