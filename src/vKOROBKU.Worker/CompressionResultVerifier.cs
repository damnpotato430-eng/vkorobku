using System.Runtime.InteropServices;
using vKOROBKU.Protocol;
using vKOROBKU.Shared;

namespace vKOROBKU.Worker;

internal static class CompressionResultVerifier
{
    private const uint WofProviderFile = 2;

    internal static (int Errors, long ErrorBytes) CountErrors(
        IReadOnlyList<WorkerFile> files,
        WorkerJob job,
        CancellationToken cancellationToken)
    {
        var expectedAlgorithm = job.Operation == "compress" ? ParseAlgorithm(job.Algorithm) : null;
        var clusterSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var errors = 0;
        long errorBytes = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.Path))
            {
                errors++;
                errorBytes += file.Length;
                continue;
            }

            var hasWofBacking = TryGetWofAlgorithm(file.Path, out var actualAlgorithm);
            if (job.Operation == "decompress")
            {
                if (hasWofBacking)
                {
                    errors++;
                    errorBytes += file.Length;
                }
                continue;
            }

            if (hasWofBacking && actualAlgorithm == expectedAlgorithm)
                continue;

            if (!TryGetPhysicalSize(file.Path, out var physicalSize) || physicalSize != file.Length)
            {
                errors++;
                errorBytes += file.Length;
                continue;
            }

            var volumeRoot = Path.GetPathRoot(file.Path) ?? string.Empty;
            if (!clusterSizes.TryGetValue(volumeRoot, out var clusterSize))
            {
                clusterSize = VolumeInfo.GetClusterSize(volumeRoot);
                clusterSizes[volumeRoot] = clusterSize;
            }

            // The readability check keeps genuinely locked tiny files classified as errors.
            var smallFileLimit = Math.Max(clusterSize, CompressionHeuristics.ChunkSize(job.Algorithm));
            if (file.Length <= smallFileLimit && CompressionHeuristics.CanRead(file.Path))
                continue;

            // Already-compressed archives can also remain regular NTFS files when WOF
            // would not reduce their physical size.
            if (CompressionHeuristics.IsLikelyIncompressible(file.Path, CompressionHeuristics.ChunkSize(job.Algorithm)))
                continue;

            errors++;
            errorBytes += file.Length;
        }

        return (errors, errorBytes);
    }

    internal static uint? ParseAlgorithm(string? algorithm) => algorithm switch
    {
        "XPRESS4K" => 0,
        "LZX" => 1,
        "XPRESS8K" => 2,
        "XPRESS16K" => 3,
        _ => null
    };

    internal static bool TryGetWofAlgorithm(string path, out uint algorithm)
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

    private static bool TryGetPhysicalSize(string path, out long physicalSize) =>
        PhysicalFileSize.TryGet(path, out physicalSize, out _);

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
