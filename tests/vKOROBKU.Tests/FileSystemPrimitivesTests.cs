using vKOROBKU.Shared;

namespace vKOROBKU.Tests;

public sealed class FileSystemPrimitivesTests
{
    [Fact]
    public void Walk_EnumeratesNestedFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[10]);
            var nested = Directory.CreateDirectory(Path.Combine(root, "sub", "deep")).FullName;
            File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[20]);

            var seen = new List<string>();
            long total = 0;
            FileSystemWalker.Walk(root, info =>
            {
                seen.Add(info.Name);
                total += info.Length;
            });

            Assert.Equal(2, seen.Count);
            Assert.Contains("a.bin", seen);
            Assert.Contains("b.bin", seen);
            Assert.Equal(30, total);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Walk_MissingRoot_YieldsNothing()
    {
        var seen = 0;
        FileSystemWalker.Walk(Path.Combine(Path.GetTempPath(), $"vkorobku-none-{Guid.NewGuid():N}"), _ => seen++);

        Assert.Equal(0, seen);
    }

    [Fact]
    public void Walk_Cancellation_Throws()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[1]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => FileSystemWalker.Walk(root, _ => { }, cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryGet_ExistingFile_ReturnsSize()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "data.bin");
            File.WriteAllBytes(path, new byte[4096]);

            Assert.True(PhysicalFileSize.TryGet(path, out var size, out var error));
            Assert.True(size >= 0);
            Assert.Equal(0, error);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryGet_MissingFile_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"vkorobku-missing-{Guid.NewGuid():N}.bin");

        Assert.False(PhysicalFileSize.TryGet(missing, out _, out var error));
        Assert.NotEqual(0, error);
    }

    [Fact]
    public void GetOrDefault_MissingFile_ReturnsFallback()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"vkorobku-missing-{Guid.NewGuid():N}.bin");

        Assert.Equal(777, PhysicalFileSize.GetOrDefault(missing, 777));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vkorobku-walk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
