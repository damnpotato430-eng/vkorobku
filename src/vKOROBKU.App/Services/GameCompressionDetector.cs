using System.Runtime.InteropServices;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class GameCompressionDetector
{
    private const uint WofProviderFile = 2;

    public Task<GameCompressionDetection> DetectAsync(
        string rootPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(rootPath, cancellationToken), cancellationToken);

    private static GameCompressionDetection Detect(string rootPath, CancellationToken cancellationToken)
    {
        var algorithms = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var physicalSizeService = new PhysicalSizeService();
        long logicalBytes = 0;
        long physicalBytes = 0;
        var compressedFiles = 0;
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        logicalBytes += info.Length;
                        try { physicalBytes += physicalSizeService.GetAllocatedSize(path); }
                        catch { physicalBytes += info.Length; }

                        if ((info.Attributes & FileAttributes.Compressed) != 0)
                        {
                            AddBytes(algorithms, "NTFS", info.Length);
                            compressedFiles++;
                            continue;
                        }

                        if (TryGetWofAlgorithm(path, out var algorithm))
                        {
                            AddBytes(algorithms, algorithm, info.Length);
                            compressedFiles++;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (algorithms.Count == 0)
            return new GameCompressionDetection(
                GameCompressionState.Uncompressed, null, 0, physicalBytes, logicalBytes, 0);

        var dominant = algorithms.MaxBy(pair => pair.Value).Key;
        return new GameCompressionDetection(
            GameCompressionState.Compressed,
            dominant,
            Math.Max(0, logicalBytes - physicalBytes),
            physicalBytes,
            logicalBytes,
            compressedFiles);
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
