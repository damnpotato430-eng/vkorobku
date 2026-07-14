using vKOROBKU.App.Models;

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
                        var attributes = info.Attributes;
                        var excluded = (attributes & (FileAttributes.Encrypted | FileAttributes.ReparsePoint | FileAttributes.SparseFile)) != 0;
                        long physical;
                        try { physical = physicalSizeService.GetAllocatedSize(path); }
                        catch { physical = info.Length; }

                        files.Add(new FileInventoryEntry(
                            path, info.Length, physical, !excluded && info.Length > 0,
                            skipExtensions?.Contains(Path.GetExtension(path)) == true));
                        if (files.Count % 2000 == 0)
                            progress?.Report($"Просканировано файлов: {files.Count:N0}");
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

        return files;
    }
}
