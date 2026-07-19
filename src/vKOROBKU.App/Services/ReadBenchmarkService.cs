using vKOROBKU.App.Resources;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace vKOROBKU.App.Services;

/// <summary>Measures physical reads while bypassing the Windows file cache.</summary>
public sealed class ReadBenchmarkService
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FlagNoBuffering = 0x20000000;
    private const uint FlagSequentialScan = 0x08000000;
    private const int BufferSize = 4 * 1024 * 1024;
    private const long LargeSampleThresholdBytes = 256L * 1024 * 1024;

    // Three passes allow a median that rejects a single noisy background-I/O spike.
    private const int NormalPassCount = 3;

    // Two passes keep large analyses reasonably short; the faster pass is used because
    // incidental competing I/O can only increase elapsed time in this uncached benchmark.
    private const int LargeSamplePassCount = 2;

    public double MeasureLogicalMegabytesPerSecond(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken,
        IProgress<ReadBenchmarkProgress>? progress = null)
    {
        long sampleBytes = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sampleBytes += new FileInfo(path).Length;
        }

        var passCount = sampleBytes > LargeSampleThresholdBytes ? LargeSamplePassCount : NormalPassCount;
        var measurements = new double[passCount];
        for (var pass = 0; pass < passCount; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPass = pass;
            measurements[pass] = MeasureSinglePass(paths, cancellationToken, bytesRead =>
                progress?.Report(new ReadBenchmarkProgress(
                    currentPass + 1,
                    passCount,
                    Math.Min(sampleBytes, bytesRead),
                    sampleBytes)));
        }

        if (passCount == LargeSamplePassCount)
            return measurements.Max();

        Array.Sort(measurements);
        return measurements[measurements.Length / 2];
    }

    private static double MeasureSinglePass(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken,
        Action<long>? reportProgress)
    {
        const long ProgressInterval = 32L * 1024 * 1024;
        long totalRead = 0;
        long nextProgress = ProgressInterval;
        var timer = Stopwatch.StartNew();
        var buffer = VirtualAlloc(IntPtr.Zero, BufferSize, 0x1000 | 0x2000, 0x04);
        if (buffer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var handle = CreateFileW(
                    path,
                    GenericRead,
                    ShareRead | ShareWrite | ShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FlagNoBuffering | FlagSequentialScan,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), string.Format(Strings.Benchmark_ReadFailed, path));

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ReadFile(handle, buffer, BufferSize, out var bytesRead, IntPtr.Zero))
                    {
                        var error = Marshal.GetLastWin32Error();
                        // ERROR_HANDLE_EOF is a normal end condition for an unbuffered read.
                        if (error == 38)
                            break;
                        throw new Win32Exception(error);
                    }
                    if (bytesRead == 0)
                        break;
                    totalRead += bytesRead;
                    if (totalRead >= nextProgress)
                    {
                        reportProgress?.Invoke(totalRead);
                        nextProgress = totalRead + ProgressInterval;
                    }
                }
            }
            reportProgress?.Invoke(totalRead);
        }
        finally
        {
            VirtualFree(buffer, UIntPtr.Zero, 0x8000);
            timer.Stop();
        }

        return totalRead / 1024d / 1024d / Math.Max(0.001, timer.Elapsed.TotalSeconds);
    }

    public sealed record ReadBenchmarkProgress(
        int Pass,
        int PassCount,
        long ProcessedBytes,
        long TotalBytes);

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
