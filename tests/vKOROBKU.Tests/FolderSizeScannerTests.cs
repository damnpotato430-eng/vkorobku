using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
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

    [Fact]
    public void Measure_ExcludesSparseFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "data.bin"), new byte[1024 * 1024]);
            // Steam leaves fully-written files with the sparse flag set after downloads;
            // the worker cannot compress them, so the scanner must not count them either.
            CreateSparseFile(Path.Combine(root, "preallocated.bin"), 512 * 1024);

            var sizes = new FolderSizeScanner().Measure(root);

            Assert.Equal(1024 * 1024, sizes.LogicalBytes);
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

    private static void CreateSparseFile(string path, int length)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite);
        if (!DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException($"FSCTL_SET_SPARSE failed: {Marshal.GetLastWin32Error()}");
        stream.Write(new byte[length]);
    }

    private const uint FsctlSetSparse = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
