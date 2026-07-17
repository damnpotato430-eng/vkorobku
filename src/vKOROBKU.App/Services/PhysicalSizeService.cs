using System.ComponentModel;
using vKOROBKU.Shared;

namespace vKOROBKU.App.Services;

public sealed class PhysicalSizeService
{
    public long GetAllocatedSize(string path)
    {
        if (!PhysicalFileSize.TryGet(path, out var size, out var win32Error))
            throw new Win32Exception(win32Error);
        return size;
    }
}
