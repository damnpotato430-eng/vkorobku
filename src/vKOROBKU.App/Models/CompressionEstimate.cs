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
    string PerformanceImpact)
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
    public string RatioText => $"Сжатие выборки: {(1 - SampleRatio) * 100:0.#}%";
    public string PerformanceText => $"{PerformanceImpact} · {ReadMegabytesPerSecond:0} МБ/с";
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
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
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
