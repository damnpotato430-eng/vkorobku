using System.ComponentModel;

namespace vKOROBKU.App.Models;

public sealed record ComputerInfo(
    string Processor,
    int LogicalProcessorCount,
    ulong MemoryBytes,
    string OperatingSystem,
    IReadOnlyList<DriveSummary> Drives)
{
    public string ProcessorText => $"{Processor} · {LogicalProcessorCount} потоков";
    public string MemoryText => $"Оперативная память: {MemoryBytes / 1024d / 1024d / 1024d:0.#} ГБ";
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

    public string DisplayText => $"{Name}  {FileSystem} · свободно {FreeBytes / 1024d / 1024d / 1024d:0.#} ГБ";

    public string SavingsText => SavedBytes > 0 ? $"сэкономлено {ByteFormatter.Format(SavedBytes)}" : string.Empty;
}
