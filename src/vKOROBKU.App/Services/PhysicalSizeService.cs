using System.ComponentModel;
using System.Runtime.InteropServices;

namespace vKOROBKU.App.Services;

public sealed class PhysicalSizeService
{
    public long GetAllocatedSize(string path)
    {
        Marshal.SetLastPInvokeError(0);
        uint high;
        var low = GetCompressedFileSizeW(path, out high);
        if (low == uint.MaxValue)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
                throw new Win32Exception(error);
        }

        return checked((long)(((ulong)high << 32) | low));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);
}
