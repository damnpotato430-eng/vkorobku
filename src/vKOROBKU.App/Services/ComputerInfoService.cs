using System.Runtime.InteropServices;
using Microsoft.Win32;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class ComputerInfoService
{
    public ComputerInfo GetComputerInfo()
    {
        var processor = ReadProcessorName();
        var memory = GetMemoryBytes();
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => new DriveSummary(
                drive.Name,
                drive.DriveFormat,
                drive.TotalSize,
                drive.AvailableFreeSpace))
            .ToArray();

        return new ComputerInfo(
            processor,
            Environment.ProcessorCount,
            memory,
            RuntimeInformation.OSDescription,
            drives);
    }

    private static string ReadProcessorName()
    {
        const string keyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Неизвестный процессор";
    }

    private static ulong GetMemoryBytes()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? status.TotalPhysical : 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
