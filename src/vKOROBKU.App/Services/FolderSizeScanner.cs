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
        // The measurement must cover exactly the files the worker manages, otherwise
        // the watch list reports phantom "degradation" that recompression can never
        // fix. The worker excludes sparse and encrypted files entirely (compact.exe
        // cannot convert them — Steam leaves fully-written files with a stale sparse
        // flag after downloads) and, with the skip list on, files no larger than one
        // cluster.
        var clusterSize = skipExtensions is null
            ? 0
            : VolumeInfo.GetClusterSize(Path.GetPathRoot(Path.GetFullPath(rootPath)) ?? string.Empty);

        FileSystemWalker.Walk(rootPath, info =>
        {
            const FileAttributes excluded =
                FileAttributes.ReparsePoint | FileAttributes.SparseFile | FileAttributes.Encrypted;
            if (info.Length <= clusterSize ||
                skipExtensions?.Contains(info.Extension) == true ||
                (info.Attributes & excluded) != 0)
                return;
            logicalBytes += info.Length;
            physicalBytes += PhysicalFileSize.GetOrDefault(info.FullName, info.Length);
        }, cancellationToken);

        return (logicalBytes, physicalBytes);
    }
}
