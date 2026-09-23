using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public enum GamePrimaryAction { Analyze, Compress, Finish, Decompress }

/// <summary>The simple UI always separates measurement from changing game files.</summary>
public static class GameActionPolicy
{
    public static GamePrimaryAction Resolve(GameCompressionState state, string? algorithm, bool hasFreshAnalysis) =>
        state switch
        {
            GameCompressionState.Compressed => GamePrimaryAction.Decompress,
            GameCompressionState.PartiallyCompressed when WatchedGamesCoordinator.IsResumableAlgorithm(algorithm)
                => GamePrimaryAction.Finish,
            _ => hasFreshAnalysis ? GamePrimaryAction.Compress : GamePrimaryAction.Analyze
        };
}
