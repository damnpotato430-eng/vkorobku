using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

public sealed class FolderSizeScannerTests
{
    [Fact]
    public void Measure_WithSkipSet_ExcludesListedExtensionsAndClusterSmallFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "data.pak"), new byte[1024 * 1024]);
            File.WriteAllBytes(Path.Combine(root, "tiny.txt"), new byte[512]);
            File.WriteAllBytes(Path.Combine(root, "video.mp4"), new byte[200 * 1024]);
            var skipSet = new HashSet<string>([".mp4"], StringComparer.OrdinalIgnoreCase);

            var withSkip = new FolderSizeScanner().Measure(root, skipSet);
            var withoutSkip = new FolderSizeScanner().Measure(root);

            // Skip-aware: the media file and the below-cluster tiny file are excluded,
            // matching what the worker would actually compress.
            Assert.Equal(1024 * 1024, withSkip.LogicalBytes);
            // Skip disabled: everything is counted.
            Assert.Equal(1024 * 1024 + 512 + 200 * 1024, withoutSkip.LogicalBytes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vkorobku-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
