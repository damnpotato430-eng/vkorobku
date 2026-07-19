using System.ComponentModel;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Models;

public sealed record ComputerInfo(
    string Processor,
    int LogicalProcessorCount,
    ulong MemoryBytes,
    string OperatingSystem,
    IReadOnlyList<DriveSummary> Drives)
{
    public string ProcessorText => string.Format(Strings.System_ProcessorThreads, Processor, LogicalProcessorCount);
    public string MemoryText => string.Format(Strings.System_Memory, ByteFormatter.Format((long)MemoryBytes));
}

public sealed class DriveSummary(
    string name,
    string fileSystem,
    long totalBytes,
    long freeBytes) : INotifyPropertyChanged
{
    private long _savedBytes;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;
    public string FileSystem { get; } = fileSystem;
    public long TotalBytes { get; } = totalBytes;
    public long FreeBytes { get; } = freeBytes;

    public long SavedBytes
    {
        get => _savedBytes;
        set
        {
            if (_savedBytes == value)
                return;
            _savedBytes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SavedBytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SavingsText)));
        }
    }

    public string DisplayText => string.Format(Strings.Drive_Display, Name, FileSystem, ByteFormatter.Format(FreeBytes));

    public string SavingsText => SavedBytes > 0
        ? string.Format(Strings.Drive_Savings, ByteFormatter.Format(SavedBytes))
        : string.Empty;
}
