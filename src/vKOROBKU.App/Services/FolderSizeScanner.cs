namespace vKOROBKU.App.Services;

/// <summary>Measures logical and physical folder size without WOF probing.</summary>
public sealed class FolderSizeScanner
{
    private readonly PhysicalSizeService _physicalSizeService = new();

    public (long LogicalBytes, long PhysicalBytes) Measure(string rootPath, CancellationToken cancellationToken = default)
    {
        long logicalBytes = 0;
        long physicalBytes = 0;
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
                        try { physicalBytes += _physicalSizeService.GetAllocatedSize(path); }
                        catch { physicalBytes += info.Length; }
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

        return (logicalBytes, physicalBytes);
    }
}
