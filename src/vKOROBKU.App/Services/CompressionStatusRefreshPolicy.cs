using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public static class CompressionStatusRefreshPolicy
{
    public static SavedCompressionStatus Create(GameInfo game, SavedCompressionStatus? previous,
        GameCompressionDetection detected, bool hasDirectStorage, DateTimeOffset checkedAt)
    {
        var buildChanged = detected.State == GameCompressionState.Compressed &&
                           previous?.State is GameCompressionState.Compressed or GameCompressionState.PartiallyCompressed &&
                           !string.IsNullOrWhiteSpace(previous.SteamBuildId) &&
                           !string.IsNullOrWhiteSpace(game.SteamBuildId) &&
                           !string.Equals(previous.SteamBuildId, game.SteamBuildId, StringComparison.Ordinal);
        return new SavedCompressionStatus(game.InstallPath,
            buildChanged ? GameCompressionState.PartiallyCompressed : detected.State,
            detected.Algorithm, checkedAt, detected.SavedBytes, detected.PhysicalBytes,
            detected.LogicalBytes, detected.CompressedFiles,
            buildChanged ? previous?.SteamBuildId : game.SteamBuildId, hasDirectStorage);
    }
}
