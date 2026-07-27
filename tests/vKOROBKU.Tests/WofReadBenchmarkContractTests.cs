using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.Tests;

/// <summary>Pins the platform behaviour the read benchmark depends on: an
/// FILE_FLAG_NO_BUFFERING read of a WOF-compressed file must be transparently
/// decompressed by wof.sys, yielding the original bytes at full logical length.
/// If it ever bypassed the filter and returned sparse zeros instead, the "after
/// compression" figures would be fiction while still looking plausible.</summary>
public sealed class WofReadBenchmarkContractTests : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001, ShareWrite = 0x00000002, ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FlagNoBuffering = 0x20000000, FlagSequentialScan = 0x08000000;
    private const int BufferSize = 4 * 1024 * 1024;
    private const int SampleBytes = 16 * 1024 * 1024;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"vkorobku-wof-{Guid.NewGuid():N}");

    [Fact]
    public async Task WofCompressedFile_ReadsBackIntactThroughTheUncachedPath()
    {
        if (!IsNtfs(_directory))
            return; // WOF needs NTFS; nothing to assert on other file systems.

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "sample.bin");
        var expected = CreateCompressibleFile(path);

        var benchmark = new ReadBenchmarkService();
        var baselineSpeed = benchmark.MeasureLogicalMegabytesPerSecond([path], CancellationToken.None);
        Assert.True(baselineSpeed > 0, "Скорость чтения до сжатия должна быть положительной");

        await new CompactProcessService().CompressAsync(
            _directory, CompressionAlgorithm.Lzx, CancellationToken.None);

        var logicalBytes = new FileInfo(path).Length;
        var physicalBytes = new PhysicalSizeService().GetAllocatedSize(path);
        if (physicalBytes >= logicalBytes)
            return; // compact declined to apply WOF here (policy, quota, driver) — nothing to verify.

        var (totalRead, head) = ReadUnbuffered(path);

        // The two assertions that matter: the filter returned everything the file
        // logically holds, and it returned the real content rather than zeros.
        Assert.Equal(logicalBytes, totalRead);
        Assert.Equal(expected, head);

        var compressedSpeed = benchmark.MeasureLogicalMegabytesPerSecond([path], CancellationToken.None);
        Assert.True(
            compressedSpeed > 0 && double.IsFinite(compressedSpeed),
            $"Скорость чтения сжатого файла должна быть конечной и положительной, получено {compressedSpeed}");
    }

    /// <summary>Writes text-like content that WOF compresses well, and returns the
    /// first bytes so the post-compression read can be compared against them.</summary>
    private static byte[] CreateCompressibleFile(string path)
    {
        var line = "vKOROBKU measures uncached reads of Windows Overlay Filter backed files. "u8.ToArray();
        var block = new byte[1024 * 1024];
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, block.Length))
        {
            for (var written = 0; written < SampleBytes; written += block.Length)
            {
                for (var index = 0; index < block.Length; index++)
                    block[index] = line[(index + written / block.Length) % line.Length];
                stream.Write(block, 0, block.Length);
            }
        }

        var head = new byte[64];
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read);
        reader.ReadExactly(head, 0, head.Length);
        return head;
    }

    private static (long TotalRead, byte[] Head) ReadUnbuffered(string path)
    {
        var buffer = VirtualAlloc(IntPtr.Zero, BufferSize, 0x1000 | 0x2000, 0x04);
        if (buffer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var head = new byte[64];
        long totalRead = 0;
        try
        {
            using var handle = CreateFileW(
                path, GenericRead, ShareRead | ShareWrite | ShareDelete,
                IntPtr.Zero, OpenExisting, FlagNoBuffering | FlagSequentialScan, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var captured = false;
            while (true)
            {
                if (!ReadFile(handle, buffer, BufferSize, out var bytesRead, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 38) // ERROR_HANDLE_EOF
                        break;
                    throw new Win32Exception(error);
                }
                if (bytesRead == 0)
                    break;
                if (!captured)
                {
                    Marshal.Copy(buffer, head, 0, head.Length);
                    captured = true;
                }
                totalRead += bytesRead;
            }
        }
        finally
        {
            VirtualFree(buffer, UIntPtr.Zero, 0x8000);
        }

        return (totalRead, head);
    }

    private static bool IsNtfs(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return false;
            var drive = new DriveInfo(root);
            return drive.IsReady && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int bytesToRead, out int bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, int size, uint allocationType, uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);
}
