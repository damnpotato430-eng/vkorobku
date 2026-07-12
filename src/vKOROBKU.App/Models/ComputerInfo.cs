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

public sealed record DriveSummary(
    string Name,
    string FileSystem,
    long TotalBytes,
    long FreeBytes)
{
    public string DisplayText => $"{Name}  {FileSystem} · свободно {FreeBytes / 1024d / 1024d / 1024d:0.#} ГБ";
}
