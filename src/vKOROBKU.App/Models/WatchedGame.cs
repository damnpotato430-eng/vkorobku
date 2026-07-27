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
    //
    // The two thresholds are independent triggers, not a combined gate. Requiring both
    // made them cancel each other out on exactly the games worth recompressing: Dota 2
    // earns ~33 GB from compression, so an update writing 1.2 GB of fresh uncompressed
    // files is only a 3.8% decay and stayed silent under a 5% share threshold — the
    // better a game compresses, the more absolute space it could hide behind a
    // relative test. Either a large relative drift or a worthwhile absolute amount is
    // now reason enough to offer finishing it.
    public bool NeedsRecompression(double decayThreshold, long minimumSavingsBytes) =>
        !HasDirectStorage &&
        PotentialSavingsBytes > 0 &&
        (DecayPercentage > decayThreshold || PotentialSavingsBytes > minimumSavingsBytes);
}
