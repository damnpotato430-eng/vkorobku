namespace vKOROBKU.App.Models;

public sealed record FileInventoryEntry(
    string FullPath,
    long LogicalBytes,
    long PhysicalBytes,
    bool CanSample);

public sealed record SampleFragment(
    string SourcePath,
    long Offset,
    int Length,
    string Extension);
