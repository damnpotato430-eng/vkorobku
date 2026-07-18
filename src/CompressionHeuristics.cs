using System.IO.Compression;

namespace vKOROBKU.Shared;

/// <summary>Shared rules for judging files compact.exe deliberately leaves without WOF
/// backing (linked source file, like WorkerProtocol.cs): the worker's result verifier
/// and the app's state detector must agree, otherwise a game full of incompressible
/// content ping-pongs between "compressed" after an operation and "partially
/// compressed" after a re-detection.</summary>
public static class CompressionHeuristics
{
    private const int ProbeBytes = 1024 * 1024;
    // WOF compresses independent chunks stored cluster-aligned: a 16K chunk squeezed
    // to 13K still occupies 16K on disk, so a file "compressible" by a plain ratio
    // can gain nothing from compact. The probe models that: per-chunk savings after
    // 4K alignment must reach this share of the window, or the file is left alone.
    // The bar sits at 15% because Deflate over-predicts the XPRESS family — files
    // compact measurably declined still show up to ~12% in this model, while
    // genuinely uncompressed game content scores far higher.
    private const double MinimumWofGainShare = 0.15;
    private const int ModelClusterSize = 4096;

    /// <summary>compact.exe leaves files no larger than one compression chunk without
    /// WOF backing when the metadata would cancel out the saving.</summary>
    public static long ChunkSize(string? algorithm) => algorithm switch
    {
        "XPRESS4K" => 4 * 1024,
        "XPRESS8K" => 8 * 1024,
        "XPRESS16K" => 16 * 1024,
        "LZX" => 32 * 1024,
        _ => 0
    };

    public static bool CanRead(string path)
    {
        try
        {
            using var source = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = source.ReadByte();
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Judges whether compact.exe would decline to compress the file, by
    /// emulating WOF economics over a megabyte from the middle of the file (headers
    /// often compress well while the bulk does not): the window is compressed in
    /// independent chunks of the algorithm's size, each rounded up to whole clusters,
    /// and only cluster-crossing gains count as savings.</summary>
    public static bool IsLikelyIncompressible(string path, long compressionChunkSize)
    {
        var chunk = (int)Math.Clamp(
            compressionChunkSize <= 0 ? 16 * 1024 : compressionChunkSize, ModelClusterSize, 64 * 1024);
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(source.Length, ProbeBytes);
            if (length == 0)
                return true;
            source.Position = Math.Max(0, (source.Length - length) / 2);

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

            long savedBytes = 0;
            for (var offset = 0; offset + chunk <= totalRead; offset += chunk)
            {
                using var output = new MemoryStream(chunk);
                using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, true))
                    compressor.Write(buffer, offset, chunk);
                var alignedLength = (output.Length + ModelClusterSize - 1) / ModelClusterSize * ModelClusterSize;
                if (alignedLength < chunk)
                    savedBytes += chunk - alignedLength;
            }
            return savedBytes < totalRead * MinimumWofGainShare;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
