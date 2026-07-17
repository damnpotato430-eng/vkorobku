using System.Runtime.InteropServices;

namespace vKOROBKU.Shared;

/// <summary>Depth-first file walk shared by the app and the worker (linked source file,
/// like WorkerProtocol.cs): never descends into reparse-point directories and tolerates
/// per-entry IO/access errors — the behavior every consumer relies on. File-level
/// filtering (reparse, sparse, extensions) stays with the caller, because inventory
/// intentionally counts files that sizing and detection skip.</summary>
public static class FileSystemWalker
{
    public static void Walk(string rootPath, Action<FileInfo> onFile, CancellationToken cancellationToken = default)
    {
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
                        onFile(new FileInfo(path));
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
    }
}

/// <summary>Physical (allocated) file size via GetCompressedFileSizeW — the single
/// P/Invoke shared by the app and the worker.</summary>
public static class PhysicalFileSize
{
    public static bool TryGet(string path, out long size, out int win32Error)
    {
        size = 0;
        Marshal.SetLastPInvokeError(0);
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == uint.MaxValue)
        {
            win32Error = Marshal.GetLastWin32Error();
            if (win32Error != 0)
                return false;
        }
        else
        {
            win32Error = 0;
        }

        size = checked((long)(((ulong)high << 32) | low));
        return true;
    }

    public static long GetOrDefault(string path, long fallback) =>
        TryGet(path, out var size, out _) ? size : fallback;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);
}
