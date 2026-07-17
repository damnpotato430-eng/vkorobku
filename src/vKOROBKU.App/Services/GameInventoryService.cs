using vKOROBKU.App.Models;
using vKOROBKU.Shared;

namespace vKOROBKU.App.Services;

public sealed class GameInventoryService(PhysicalSizeService physicalSizeService)
{
    public IReadOnlyList<FileInventoryEntry> CreateInventory(
        string rootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        ISet<string>? skipExtensions = null)
    {
        var files = new List<FileInventoryEntry>();

        FileSystemWalker.Walk(rootPath, info =>
        {
            var attributes = info.Attributes;
            var excluded = (attributes & (FileAttributes.Encrypted | FileAttributes.ReparsePoint | FileAttributes.SparseFile)) != 0;
            long physical;
            try { physical = physicalSizeService.GetAllocatedSize(info.FullName); }
            catch { physical = info.Length; }

            files.Add(new FileInventoryEntry(
                info.FullName, info.Length, physical, !excluded && info.Length > 0,
                skipExtensions?.Contains(info.Extension) == true));
            if (files.Count % 2000 == 0)
                progress?.Report($"Просканировано файлов: {files.Count:N0}");
        }, cancellationToken);

        return files;
    }
}
