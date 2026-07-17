using vKOROBKU.Shared;

namespace vKOROBKU.App.Services;

/// <summary>Measures logical and physical folder size without WOF probing.</summary>
public sealed class FolderSizeScanner
{
    public (long LogicalBytes, long PhysicalBytes) Measure(
        string rootPath,
        ISet<string>? skipExtensions = null,
        CancellationToken cancellationToken = default)
    {
        long logicalBytes = 0;
        long physicalBytes = 0;
        // The worker also skips files no larger than one cluster during compression,
        // so watch-list measurements must exclude them too — otherwise games with many
        // tiny files show phantom "degradation" that recompression can never fix.
        var clusterSize = skipExtensions is null
            ? 0
            : VolumeInfo.GetClusterSize(Path.GetPathRoot(Path.GetFullPath(rootPath)) ?? string.Empty);

        FileSystemWalker.Walk(rootPath, info =>
        {
            if (info.Length <= clusterSize ||
                skipExtensions?.Contains(info.Extension) == true ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return;
            logicalBytes += info.Length;
            physicalBytes += PhysicalFileSize.GetOrDefault(info.FullName, info.Length);
        }, cancellationToken);

        return (logicalBytes, physicalBytes);
    }
}
