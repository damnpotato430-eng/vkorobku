using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

/// <summary>A source of installed games (a launcher). Implementations must not throw for
/// a missing or unreadable library — they return an empty list instead.</summary>
public interface IGameScanner
{
    Task<IReadOnlyList<GameInfo>> ScanAsync(CancellationToken cancellationToken = default);
}
