namespace vKOROBKU.App.Models;

public sealed record SavedGameAnalysis(
    string InstallPath,
    DateTimeOffset AnalyzedAt,
    GameAnalysisResult Result);
