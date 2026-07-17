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

        FileSystemWalker.Walk(rootPath, info =>
        {
            if (skipExtensions?.Contains(info.Extension) == true ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return;
            logicalBytes += info.Length;
            physicalBytes += PhysicalFileSize.GetOrDefault(info.FullName, info.Length);
        }, cancellationToken);

        return (logicalBytes, physicalBytes);
    }
}
