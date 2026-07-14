namespace vKOROBKU.App.Models;

/// <summary>A game compressed through vKOROBKU whose saving is re-checked on startup.</summary>
public sealed record WatchedGame(
    string FolderPath,
    string DisplayName,
    bool IsSteamGame,
    string? SteamAppId,
    string? SteamBuildId,
    string Algorithm,
    DateTimeOffset LastCompressedAtUtc,
    long LastCompressedSize,
    long LastUncompressedSize,
    long LastCheckedSize,
    DateTimeOffset LastCheckedAtUtc,
    bool HasDirectStorage = false)
{
    /// <summary>
    /// Share of the earned saving lost to updates writing uncompressed files:
    /// 0 — the game is as compressed as right after the operation, 1 — the saving is gone.
    /// </summary>
    public double DecayPercentage
    {
        get
        {
            var denominator = LastUncompressedSize - LastCompressedSize;
            if (denominator <= 0)
                return 0;
            return Math.Clamp((LastCheckedSize - LastCompressedSize) / (double)denominator, 0, 1);
        }
    }

    public long PotentialSavingsBytes => Math.Max(0, LastCheckedSize - LastCompressedSize);

    // DirectStorage games are excluded from recompression offers: NTFS compression
    // breaks their fast read path, so the app never recommends compressing them.
    public bool NeedsRecompression(double decayThreshold, long minimumSavingsBytes) =>
        !HasDirectStorage &&
        DecayPercentage > decayThreshold &&
        PotentialSavingsBytes > minimumSavingsBytes;
}
