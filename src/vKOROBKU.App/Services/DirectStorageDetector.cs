namespace vKOROBKU.App.Services;

/// <summary>Detects DirectStorage titles by their runtime libraries next to game binaries.</summary>
public sealed class DirectStorageDetector
{
    // The runtime ships beside the executable, so a shallow scan is sufficient
    // and keeps the check cheap even for very large installations.
    private const int MaximumDepth = 4;
    private static readonly string[] MarkerFiles = ["dstorage.dll", "dstoragecore.dll"];

    public bool Detect(string rootPath, CancellationToken cancellationToken = default)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((rootPath, 0));

        while (pending.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var marker in MarkerFiles)
                {
                    if (File.Exists(Path.Combine(item.Path, marker)))
                        return true;
                }

                if (item.Depth >= MaximumDepth)
                    continue;
                foreach (var child in Directory.EnumerateDirectories(item.Path))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Enqueue((child, item.Depth + 1));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return false;
    }
}
