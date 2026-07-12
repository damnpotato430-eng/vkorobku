namespace vKOROBKU.App.Models;

public sealed record ManualGameRecord(
    string InstallPath,
    string Name,
    string? SteamAppId,
    long LogicalSizeBytes,
    DateTimeOffset AddedAt);

public sealed record DetectedGameIdentity(
    string Name,
    string? SteamAppId,
    string DetectionSource);
