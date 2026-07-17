using vKOROBKU.Shared;

namespace vKOROBKU.App.Services;

public sealed class FileTreeService
{
    public long CalculateLogicalSize(string rootPath, CancellationToken cancellationToken = default)
    {
        long total = 0;
        FileSystemWalker.Walk(rootPath, info => total += info.Length, cancellationToken);
        return total;
    }
}
