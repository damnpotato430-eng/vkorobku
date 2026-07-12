using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class GameAnalysisService
{
    private readonly PhysicalSizeService _physicalSizeService = new();
    private readonly SamplePlanner _samplePlanner = new();
    private readonly CompactProcessService _compactService = new();
    private readonly ReadBenchmarkService _readBenchmark = new();

    public async Task<GameAnalysisResult> AnalyzeAsync(
        GameInfo game,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        long maximumSampleBytes = 0)
    {
        progress?.Report("Проверяем файловую систему…");
        EnsureSupportedFileSystem(game.InstallPath);

        var inventoryService = new GameInventoryService(_physicalSizeService);
        var inventory = await Task.Run(
            () => inventoryService.CreateInventory(game.InstallPath, progress, cancellationToken),
            cancellationToken);

        var logicalBytes = inventory.Sum(file => file.LogicalBytes);
        var physicalBytes = inventory.Sum(file => file.PhysicalBytes);
        var excludedPhysicalBytes = inventory.Where(file => !file.CanSample).Sum(file => file.PhysicalBytes);
        var eligibleLogicalBytes = inventory.Where(file => file.CanSample).Sum(file => file.LogicalBytes);
        if (eligibleLogicalBytes == 0)
            throw new InvalidOperationException("В папке нет доступных файлов для анализа.");

        if (maximumSampleBytes <= 0)
            maximumSampleBytes = SelectAutomaticSampleLimit(eligibleLogicalBytes);

        progress?.Report($"Формируем выборку до {ByteFormatter.Format(maximumSampleBytes)}…");
        var plan = _samplePlanner.CreatePlan(inventory, maximumSampleBytes);
        if (plan.Count == 0)
            throw new InvalidOperationException("Не удалось сформировать выборку файлов.");

        var workspace = CreateWorkspace(plan.Sum(fragment => (long)fragment.Length));
        try
        {
            var sampleFiles = await CopySampleAsync(plan, workspace, progress, cancellationToken);
            var sampleBytes = sampleFiles.Sum(path => new FileInfo(path).Length);
            progress?.Report("Измеряем скорость чтения без сжатия…");
            var baselineReadSpeed = _readBenchmark.MeasureLogicalMegabytesPerSecond(sampleFiles, cancellationToken);
            var estimates = new List<CompressionEstimate>();
            var algorithms = Enum.GetValues<CompressionAlgorithm>();

            foreach (var algorithm in algorithms)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Проверяем {GetAlgorithmName(algorithm)}…");
                await _compactService.CompressAsync(workspace, algorithm, cancellationToken);

                long compressedBytes = 0;
                foreach (var sampleFile in sampleFiles)
                    compressedBytes += _physicalSizeService.GetAllocatedSize(sampleFile);

                var ratio = sampleBytes == 0 ? 1 : compressedBytes / (double)sampleBytes;
                ratio = Math.Clamp(ratio, 0, 1.25);
                progress?.Report($"Измеряем чтение {GetAlgorithmName(algorithm)}…");
                var readSpeed = _readBenchmark.MeasureLogicalMegabytesPerSecond(sampleFiles, cancellationToken);
                estimates.Add(CreateEstimate(
                    algorithm,
                    ratio,
                    sampleBytes,
                    eligibleLogicalBytes,
                    excludedPhysicalBytes,
                    physicalBytes,
                    plan.Count,
                    baselineReadSpeed,
                    readSpeed));

                if (algorithm != algorithms[^1])
                    await _compactService.DecompressAsync(workspace, cancellationToken);
            }

            progress?.Report("Анализ завершён");
            return new GameAnalysisResult(
                logicalBytes,
                physicalBytes,
                inventory.Count,
                inventory.Count(file => !file.CanSample),
                sampleBytes,
                estimates);
        }
        finally
        {
            try { Directory.Delete(workspace, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static CompressionEstimate CreateEstimate(
        CompressionAlgorithm algorithm,
        double ratio,
        long sampleBytes,
        long eligibleLogicalBytes,
        long excludedPhysicalBytes,
        long currentPhysicalBytes,
        int fragmentCount,
        double baselineReadSpeed,
        double compressedReadSpeed)
    {
        var coverage = sampleBytes / (double)eligibleLogicalBytes;
        var (confidence, margin) = coverage >= 0.01 && fragmentCount >= 32
            ? ("Высокая", 0.03)
            : coverage >= 0.0025 && fragmentCount >= 12
                ? ("Средняя", 0.07)
                : ("Низкая", 0.12);

        var estimated = excludedPhysicalBytes + (long)(eligibleLogicalBytes * ratio);
        var optimistic = excludedPhysicalBytes + (long)(eligibleLogicalBytes * Math.Max(0, ratio - margin));
        var pessimistic = excludedPhysicalBytes + (long)(eligibleLogicalBytes * Math.Min(1.25, ratio + margin));
        var minimumSavings = Math.Max(0, currentPhysicalBytes - pessimistic);
        var maximumSavings = Math.Max(minimumSavings, currentPhysicalBytes - optimistic);

        var relativeSpeed = baselineReadSpeed <= 0 ? 1 : compressedReadSpeed / baselineReadSpeed;
        var performanceImpact = relativeSpeed switch
        {
            >= 1.08 => "Вероятно быстрее",
            < 0.88 => "Возможно медленнее",
            _ => "Изменение незаметно"
        };

        return new CompressionEstimate(
            algorithm,
            Math.Max(0, estimated),
            minimumSavings,
            Math.Max(0, maximumSavings),
            ratio,
            confidence,
            compressedReadSpeed,
            performanceImpact,
            baselineReadSpeed);
    }

    private static async Task<IReadOnlyList<string>> CopySampleAsync(
        IReadOnlyList<SampleFragment> plan,
        string workspace,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = new List<string>(plan.Count);
        var buffer = new byte[1024 * 1024];

        for (var index = 0; index < plan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fragment = plan[index];
            var extension = fragment.Extension.Length <= 16 ? fragment.Extension : ".bin";
            var destination = Path.Combine(workspace, $"sample-{index:D4}{extension}");

            await using var source = new FileStream(fragment.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, true);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true);
            source.Position = fragment.Offset;
            var remaining = fragment.Length;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0)
                    break;
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }

            if (target.Length > 0)
                result.Add(destination);
            progress?.Report($"Подготовка выборки: {index + 1} из {plan.Count}");
        }

        return result;
    }

    private static string CreateWorkspace(long requiredBytes)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vKOROBKU", "Analysis");
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        if (!drive.IsReady || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Для временной выборки требуется NTFS-диск.");
        if (drive.AvailableFreeSpace < requiredBytes + 256L * 1024 * 1024)
            throw new InvalidOperationException("Недостаточно свободного места для временной выборки.");

        Directory.CreateDirectory(root);
        var workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static void EnsureSupportedFileSystem(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new InvalidOperationException("Не удалось определить диск игры.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Анализ XPRESS/LZX поддерживается только для игр на NTFS.");
    }

    private static long SelectAutomaticSampleLimit(long gameBytes)
    {
        const long GiB = 1024L * 1024 * 1024;
        return gameBytes switch
        {
            <= 20 * GiB => 512L * 1024 * 1024,
            <= 80 * GiB => GiB,
            _ => 2 * GiB
        };
    }

    private static string GetAlgorithmName(CompressionAlgorithm algorithm) => algorithm switch
    {
        CompressionAlgorithm.Xpress4K => "XPRESS4K",
        CompressionAlgorithm.Xpress8K => "XPRESS8K",
        CompressionAlgorithm.Xpress16K => "XPRESS16K",
        CompressionAlgorithm.Lzx => "LZX",
        _ => algorithm.ToString()
    };
}
