namespace vKOROBKU.App.Models;

public sealed record AnalysisModeOption(
    string Name,
    string Description,
    long MaximumSampleBytes)
{
    public string DisplayText => $"{Name} · {Description}";
    public override string ToString() => DisplayText;
}
