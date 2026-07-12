namespace vKOROBKU.App.Models;

public sealed record SavedCompressionStatus(
    string InstallPath,
    GameCompressionState State,
    string? Algorithm,
    DateTimeOffset CheckedAt,
    long SavedBytes = 0,
    long PhysicalBytes = 0,
    long LogicalBytes = 0,
    int CompressedFiles = 0,
    string? SteamBuildId = null);

public sealed record GameCompressionDetection(
    GameCompressionState State,
    string? Algorithm,
    long SavedBytes,
    long PhysicalBytes,
    long LogicalBytes,
    int CompressedFiles);
