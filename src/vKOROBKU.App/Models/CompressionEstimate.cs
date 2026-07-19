using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Models;

public enum CompressionAlgorithm
{
    Xpress4K,
    Xpress8K,
    Xpress16K,
    Lzx
}

public sealed record CompressionEstimate(
    CompressionAlgorithm Algorithm,
    long EstimatedPhysicalBytes,
    long MinimumSavingsBytes,
    long MaximumSavingsBytes,
    double SampleRatio,
    string Confidence,
    double ReadMegabytesPerSecond,
    string PerformanceImpact,
    double BaselineReadMegabytesPerSecond = 0)
{
    public string AlgorithmText => Algorithm switch
    {
        CompressionAlgorithm.Xpress4K => "XPRESS4K",
        CompressionAlgorithm.Xpress8K => "XPRESS8K",
        CompressionAlgorithm.Xpress16K => "XPRESS16K",
        CompressionAlgorithm.Lzx => "LZX",
        _ => Algorithm.ToString()
    };

    public string EstimatedSizeText => ByteFormatter.Format(EstimatedPhysicalBytes);
    public string SavingsText => $"{ByteFormatter.Format(MinimumSavingsBytes)}–{ByteFormatter.Format(MaximumSavingsBytes)}";
    public string RatioText => string.Format(Strings.Estimate_SampleRatio, $"{(1 - SampleRatio) * 100:0.#}");
    public double ReadSpeedChangePercent => BaselineReadMegabytesPerSecond <= 0
        ? 0
        : (ReadMegabytesPerSecond / BaselineReadMegabytesPerSecond - 1) * 100;

    public string PerformanceText => BaselineReadMegabytesPerSecond <= 0
        ? Strings.Estimate_RepeatForSpeed
        : string.Format(
            Strings.Estimate_Performance,
            PerformanceImpact,
            $"{BaselineReadMegabytesPerSecond:0}",
            $"{ReadMegabytesPerSecond:0}",
            $"{ReadSpeedChangePercent:+0;-0;0}");
}

public sealed record GameAnalysisResult(
    long LogicalBytes,
    long CurrentPhysicalBytes,
    int FileCount,
    int ExcludedFileCount,
    long SampleBytes,
    IReadOnlyList<CompressionEstimate> Estimates);

public static class ByteFormatter
{
    public static string Format(long bytes)
    {
        string[] units =
        [
            Strings.Unit_Bytes, Strings.Unit_KB, Strings.Unit_MB, Strings.Unit_GB, Strings.Unit_TB
        ];
        var value = Math.Max(0, bytes);
        var display = (double)value;
        var unit = 0;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }
        return $"{display:0.#} {units[unit]}";
    }
}
