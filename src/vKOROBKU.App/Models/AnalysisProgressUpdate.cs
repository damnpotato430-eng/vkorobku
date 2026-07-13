namespace vKOROBKU.App.Models;

public sealed record AnalysisProgressUpdate(
    string Stage,
    double Percent,
    long ProcessedBytes = 0,
    long TotalBytes = 0);
