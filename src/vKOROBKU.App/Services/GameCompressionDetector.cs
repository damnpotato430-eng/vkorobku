using System.Runtime.InteropServices;
using vKOROBKU.App.Models;
using vKOROBKU.Shared;

namespace vKOROBKU.App.Services;

public sealed class GameCompressionDetector
{
    private const uint WofProviderFile = 2;

    // Games often ship a few pre-compressed files, so the state is decided by the
    // share of compressed bytes rather than by the presence of a single compressed file.
    internal const double CompressedByteShare = 0.85;
    internal const double PartiallyCompressedByteShare = 0.05;

    public Task<GameCompressionDetection> DetectAsync(
        string rootPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(rootPath, cancellationToken), cancellationToken);

    private static GameCompressionDetection Detect(string rootPath, CancellationToken cancellationToken)
    {
        var algorithms = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long logicalBytes = 0;
        long physicalBytes = 0;
        var compressedFiles = 0;

        FileSystemWalker.Walk(rootPath, info =>
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                return;

            logicalBytes += info.Length;
            physicalBytes += PhysicalFileSize.GetOrDefault(info.FullName, info.Length);

            if ((info.Attributes & FileAttributes.Compressed) != 0)
            {
                AddBytes(algorithms, "NTFS", info.Length);
                compressedFiles++;
                return;
            }

            if (TryGetWofAlgorithm(info.FullName, out var algorithm))
            {
                AddBytes(algorithms, algorithm, info.Length);
                compressedFiles++;
            }
        }, cancellationToken);

        var state = ClassifyState(algorithms.Values.Sum(), logicalBytes);
        if (state == GameCompressionState.Uncompressed)
            return new GameCompressionDetection(
                GameCompressionState.Uncompressed, null, 0, physicalBytes, logicalBytes, compressedFiles);

        var dominant = algorithms.MaxBy(pair => pair.Value).Key;
        return new GameCompressionDetection(
            state,
            dominant,
            Math.Max(0, logicalBytes - physicalBytes),
            physicalBytes,
            logicalBytes,
            compressedFiles);
    }

    internal static GameCompressionState ClassifyState(long compressedLogicalBytes, long totalLogicalBytes)
    {
        if (totalLogicalBytes <= 0 || compressedLogicalBytes <= 0)
            return GameCompressionState.Uncompressed;

        var share = compressedLogicalBytes / (double)totalLogicalBytes;
        return share >= CompressedByteShare
            ? GameCompressionState.Compressed
            : share >= PartiallyCompressedByteShare
                ? GameCompressionState.PartiallyCompressed
                : GameCompressionState.Uncompressed;
    }

    private static bool TryGetWofAlgorithm(string path, out string algorithm)
    {
        algorithm = string.Empty;
        var info = new WofFileCompressionInfo();
        uint length = (uint)Marshal.SizeOf<WofFileCompressionInfo>();
        var result = WofIsExternalFile(path, out var isExternal, out var provider, ref info, ref length);
        if (result < 0 || !isExternal || provider != WofProviderFile)
            return false;

        algorithm = info.Algorithm switch
        {
            0 => "XPRESS4K",
            1 => "LZX",
            2 => "XPRESS8K",
            3 => "XPRESS16K",
            _ => "WOF"
        };
        return true;
    }

    private static void AddBytes(IDictionary<string, long> algorithms, string algorithm, long bytes)
    {
        algorithms.TryGetValue(algorithm, out var current);
        algorithms[algorithm] = current + bytes;
    }

    [DllImport("WofUtil.dll", CharSet = CharSet.Unicode)]
    private static extern int WofIsExternalFile(
        string filePath,
        [MarshalAs(UnmanagedType.Bool)] out bool isExternalFile,
        out uint provider,
        ref WofFileCompressionInfo externalFileInfo,
        ref uint bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct WofFileCompressionInfo
    {
        public uint Algorithm;
        public uint Flags;
    }
}
