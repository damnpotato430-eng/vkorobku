using System.IO.Compression;
using System.Runtime.InteropServices;
using vKOROBKU.Protocol;

namespace vKOROBKU.Worker;

internal static class CompressionResultVerifier
{
    private const uint WofProviderFile = 2;
    private const int ProbeBytes = 1024 * 1024;
    private const double IncompressibleRatio = 0.98;

    internal static int CountErrors(
        IReadOnlyList<WorkerFile> files,
        WorkerJob job,
        CancellationToken cancellationToken)
    {
        var expectedAlgorithm = job.Operation == "compress" ? ParseAlgorithm(job.Algorithm) : null;
        var clusterSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var errors = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.Path))
            {
                errors++;
                continue;
            }

            var hasWofBacking = TryGetWofAlgorithm(file.Path, out var actualAlgorithm);
            if (job.Operation == "decompress")
            {
                if (hasWofBacking)
                    errors++;
                continue;
            }

            if (hasWofBacking && actualAlgorithm == expectedAlgorithm)
                continue;

            if (!TryGetPhysicalSize(file.Path, out var physicalSize) || physicalSize != file.Length)
            {
                errors++;
                continue;
            }

            var volumeRoot = Path.GetPathRoot(file.Path) ?? string.Empty;
            if (!clusterSizes.TryGetValue(volumeRoot, out var clusterSize))
            {
                clusterSize = GetClusterSize(volumeRoot);
                clusterSizes[volumeRoot] = clusterSize;
            }

            // compact.exe deliberately leaves tiny and already-compressed data without
            // WOF backing when storing it externally would not save disk space.
            if (file.Length < clusterSize || IsLikelyIncompressible(file.Path))
                continue;

            errors++;
        }

        return errors;
    }

    private static uint? ParseAlgorithm(string? algorithm) => algorithm switch
    {
        "XPRESS4K" => 0,
        "LZX" => 1,
        "XPRESS8K" => 2,
        "XPRESS16K" => 3,
        _ => null
    };

    private static bool TryGetWofAlgorithm(string path, out uint algorithm)
    {
        algorithm = uint.MaxValue;
        var info = new WofFileCompressionInfo();
        uint length = (uint)Marshal.SizeOf<WofFileCompressionInfo>();
        var result = WofIsExternalFile(path, out var isExternal, out var provider, ref info, ref length);
        if (result < 0 || !isExternal || provider != WofProviderFile)
            return false;
        algorithm = info.Algorithm;
        return true;
    }

    private static bool TryGetPhysicalSize(string path, out long physicalSize)
    {
        Marshal.SetLastPInvokeError(0);
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
        {
            physicalSize = 0;
            return false;
        }
        physicalSize = checked((long)(((ulong)high << 32) | low));
        return true;
    }

    private static bool IsLikelyIncompressible(string path)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(source.Length, ProbeBytes);
            if (length == 0)
                return true;

            var buffer = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                var read = source.Read(buffer, totalRead, length - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }
            if (totalRead == 0)
                return false;

            using var output = new MemoryStream(totalRead);
            using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, true))
                compressor.Write(buffer, 0, totalRead);
            return output.Length >= totalRead * IncompressibleRatio;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static long GetClusterSize(string volumeRoot)
    {
        if (GetDiskFreeSpaceW(volumeRoot, out var sectorsPerCluster, out var bytesPerSector, out _, out _))
            return checked((long)sectorsPerCluster * bytesPerSector);
        return 4096;
    }

    [DllImport("WofUtil.dll", CharSet = CharSet.Unicode)]
    private static extern int WofIsExternalFile(
        string filePath,
        [MarshalAs(UnmanagedType.Bool)] out bool isExternalFile,
        out uint provider,
        ref WofFileCompressionInfo externalFileInfo,
        ref uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(
        string rootPathName,
        out uint sectorsPerCluster,
        out uint bytesPerSector,
        out uint numberOfFreeClusters,
        out uint totalNumberOfClusters);

    [StructLayout(LayoutKind.Sequential)]
    private struct WofFileCompressionInfo
    {
        public uint Algorithm;
        public uint Flags;
    }
}
