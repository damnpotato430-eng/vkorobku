using vKOROBKU.App.Models;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App.Services;

public sealed class GameAnalysisService
{
    private readonly PhysicalSizeService _physicalSizeService = new();
    private readonly SamplePlanner _samplePlanner = new();
    private readonly CompactProcessService _compactService = new();
    private readonly ReadBenchmarkService _readBenchmark = new();

    public async Task<GameAnalysisResult> AnalyzeAsync(
        GameInfo game,
        IProgress<AnalysisProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        long maximumSampleBytes = 0,
        ISet<string>? skipExtensions = null)
    {
        progress?.Report(new AnalysisProgressUpdate(Strings.Analysis_CheckingFileSystem, 0));

        var inventoryService = new GameInventoryService(_physicalSizeService);
        var inventoryProgress = new Progress<string>(message =>
            progress?.Report(new AnalysisProgressUpdate(message, 3)));
        var inventory = await Task.Run(
            () => inventoryService.CreateInventory(game.InstallPath, inventoryProgress, cancellationToken, skipExtensions),
            cancellationToken);

        progress?.Report(new AnalysisProgressUpdate(Strings.Analysis_BuildingSample, 7));
        var prepared = await Task.Run(() =>
        {
            var logicalBytes = inventory.Sum(file => file.LogicalBytes);
            var physicalBytes = inventory.Sum(file => file.PhysicalBytes);
            var excludedPhysicalBytes = inventory.Where(file => !file.CanSample).Sum(file => file.PhysicalBytes);
            // Skip-listed files are not sampled: they stay at their current physical
            // size in the forecast instead of wasting the trial-compression budget.
            var skipPhysicalBytes = inventory
                .Where(file => file.CanSample && file.IsSkipListed)
                .Sum(file => file.PhysicalBytes);
            var eligibleLogicalBytes = inventory
                .Where(file => file.CanSample && !file.IsSkipListed)
                .Sum(file => file.LogicalBytes);
            if (eligibleLogicalBytes == 0)
                throw new InvalidOperationException(
                    Strings.Analysis_AllFilesIncompressible);

            var sampleLimit = maximumSampleBytes > 0
                ? maximumSampleBytes
                : SelectAutomaticSampleLimit(eligibleLogicalBytes);
            var plan = _samplePlanner.CreatePlan(inventory, sampleLimit);
            if (plan.Count == 0)
                throw new InvalidOperationException(Strings.Analysis_SampleFailed);

            return (logicalBytes, physicalBytes, excludedPhysicalBytes, skipPhysicalBytes, eligibleLogicalBytes, sampleLimit, plan);
        }, cancellationToken);

        progress?.Report(new AnalysisProgressUpdate(
            string.Format(Strings.Analysis_BuildingSampleUpTo, ByteFormatter.Format(prepared.sampleLimit)), 9));
        var requiredBytes = prepared.plan.Sum(fragment => (long)fragment.Length);
        var workspace = CreateWorkspace(game.InstallPath, requiredBytes);
        try
        {
            var sampleFiles = await CopySampleAsync(prepared.plan, workspace, progress, cancellationToken);
            var sampleBytes = sampleFiles.Sum(path => new FileInfo(path).Length);
            var baselineReadSpeed = await MeasureReadAsync(
                sampleFiles,
                Strings.Analysis_MeasuringBaseline,
                30,
                40,
                progress,
                cancellationToken);
            var estimates = new List<CompressionEstimate>();
            var algorithms = Enum.GetValues<CompressionAlgorithm>();

            for (var index = 0; index < algorithms.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var algorithm = algorithms[index];
                var stageStart = 40 + index * 15;
                var algorithmName = GetAlgorithmName(algorithm);
                progress?.Report(new AnalysisProgressUpdate(string.Format(Strings.Analysis_CompressingSample, algorithmName), stageStart));
                await _compactService.CompressAsync(workspace, algorithm, cancellationToken);

                progress?.Report(new AnalysisProgressUpdate(string.Format(Strings.Analysis_CountingResult, algorithmName), stageStart + 4));
                var compressedBytes = await Task.Run(
                    () => sampleFiles.Sum(path => _physicalSizeService.GetAllocatedSize(path)),
                    cancellationToken);

                var ratio = sampleBytes == 0 ? 1 : compressedBytes / (double)sampleBytes;
                ratio = Math.Clamp(ratio, 0, 1.25);
                var readSpeed = await MeasureReadAsync(
                    sampleFiles,
                    string.Format(Strings.Analysis_MeasuringRead, algorithmName),
                    stageStart + 5,
                    stageStart + 13,
                    progress,
                    cancellationToken);
                estimates.Add(CreateEstimate(
                    algorithm,
                    ratio,
                    sampleBytes,
                    prepared.eligibleLogicalBytes,
                    prepared.excludedPhysicalBytes,
                    prepared.skipPhysicalBytes,
                    prepared.physicalBytes,
                    prepared.plan.Count,
                    baselineReadSpeed,
                    readSpeed));

                if (algorithm != algorithms[^1])
                {
                    progress?.Report(new AnalysisProgressUpdate(string.Format(Strings.Analysis_PreparingNext, algorithmName), stageStart + 14));
                    await _compactService.DecompressAsync(workspace, cancellationToken);
                }
            }

            progress?.Report(new AnalysisProgressUpdate(Strings.Analysis_Done, 100, sampleBytes, sampleBytes));
            return new GameAnalysisResult(
                prepared.logicalBytes,
                prepared.physicalBytes,
                inventory.Count,
                inventory.Count(file => !file.CanSample),
                sampleBytes,
                estimates);
        }
        finally
        {
            await Task.Run(() => DeleteWorkspace(workspace), CancellationToken.None);
        }
    }

    private async Task<double> MeasureReadAsync(
        IReadOnlyList<string> paths,
        string stage,
        double startPercent,
        double endPercent,
        IProgress<AnalysisProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var benchmarkProgress = new Progress<ReadBenchmarkService.ReadBenchmarkProgress>(update =>
        {
            var totalWork = Math.Max(1L, update.TotalBytes * update.PassCount);
            var completedWork = update.TotalBytes * (update.Pass - 1L) + update.ProcessedBytes;
            var fraction = Math.Clamp(completedWork / (double)totalWork, 0, 1);
            var percent = startPercent + (endPercent - startPercent) * fraction;
            progress?.Report(new AnalysisProgressUpdate(
                string.Format(Strings.Analysis_Pass, stage, update.Pass, update.PassCount),
                percent,
                completedWork,
                totalWork));
        });

        progress?.Report(new AnalysisProgressUpdate(stage, startPercent));
        return await Task.Run(
            () => _readBenchmark.MeasureLogicalMegabytesPerSecond(paths, cancellationToken, benchmarkProgress),
            cancellationToken);
    }

    private static CompressionEstimate CreateEstimate(
        CompressionAlgorithm algorithm,
        double ratio,
        long sampleBytes,
        long eligibleLogicalBytes,
        long excludedPhysicalBytes,
        long skipPhysicalBytes,
        long currentPhysicalBytes,
        int fragmentCount,
        double baselineReadSpeed,
        double compressedReadSpeed)
    {
        var coverage = sampleBytes / (double)eligibleLogicalBytes;
        var (confidence, margin) = coverage >= 0.01 && fragmentCount >= 32
            ? (Strings.Confidence_High, 0.03)
            : coverage >= 0.0025 && fragmentCount >= 12
                ? (Strings.Confidence_Medium, 0.07)
                : (Strings.Confidence_Low, 0.12);

        var untouchedPhysicalBytes = excludedPhysicalBytes + skipPhysicalBytes;
        var estimated = untouchedPhysicalBytes + (long)(eligibleLogicalBytes * ratio);
        var optimistic = untouchedPhysicalBytes + (long)(eligibleLogicalBytes * Math.Max(0, ratio - margin));
        var pessimistic = untouchedPhysicalBytes + (long)(eligibleLogicalBytes * Math.Min(1.25, ratio + margin));
        var minimumSavings = Math.Max(0, currentPhysicalBytes - pessimistic);
        var maximumSavings = Math.Max(minimumSavings, currentPhysicalBytes - optimistic);

        var relativeSpeed = baselineReadSpeed <= 0 ? 1 : compressedReadSpeed / baselineReadSpeed;
        var performanceImpact = relativeSpeed switch
        {
            >= 1.08 => Strings.Perf_LikelyFaster,
            < 0.88 => Strings.Perf_PossiblySlower,
            _ => Strings.Perf_NoChange
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
        IProgress<AnalysisProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var result = new List<string>(plan.Count);
        var buffer = new byte[1024 * 1024];
        var totalBytes = plan.Sum(fragment => (long)fragment.Length);
        long processedBytes = 0;
        var lastReportedPercent = -1;

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
                processedBytes += read;
                var percent = totalBytes == 0 ? 29 : 10 + (int)(processedBytes * 19 / totalBytes);
                if (percent != lastReportedPercent)
                {
                    lastReportedPercent = percent;
                    progress?.Report(new AnalysisProgressUpdate(
                        string.Format(Strings.Analysis_PreparingSampleIndex, index + 1, plan.Count),
                        percent,
                        processedBytes,
                        totalBytes));
                }
            }

            if (target.Length > 0)
                result.Add(destination);
        }

        return result;
    }

    private static string CreateWorkspace(string gamePath, long requiredBytes)
    {
        var gameVolumeRoot = Path.GetPathRoot(Path.GetFullPath(gamePath))
                             ?? throw new InvalidOperationException(Strings.Analysis_DriveUnknown);
        var drive = new DriveInfo(gameVolumeRoot);
        if (!drive.IsReady || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Strings.Analysis_NtfsOnly);
        if (drive.AvailableFreeSpace < requiredBytes + 256L * 1024 * 1024)
            throw new InvalidOperationException(Strings.Analysis_NotEnoughSpace);

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var systemVolumeRoot = Path.GetPathRoot(localApplicationData);
        var workspaceRoot = string.Equals(gameVolumeRoot, systemVolumeRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(localApplicationData, "vKOROBKU", "Analysis")
            : Path.Combine(gameVolumeRoot, "vKOROBKU-Analysis");

        Directory.CreateDirectory(workspaceRoot);
        File.SetAttributes(workspaceRoot, File.GetAttributes(workspaceRoot) | FileAttributes.Hidden);

        var workspace = Path.Combine(workspaceRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.SetAttributes(workspace, File.GetAttributes(workspace) | FileAttributes.Hidden);
        var activeMarker = Path.Combine(workspace, AnalysisWorkspaceCleaner.ActiveMarkerName);
        File.WriteAllText(activeMarker, Environment.ProcessId.ToString());
        File.SetAttributes(activeMarker, FileAttributes.Hidden | FileAttributes.Temporary);
        return workspace;
    }

    private static void DeleteWorkspace(string workspace)
    {
        try
        {
            Directory.Delete(workspace, true);
            var root = Directory.GetParent(workspace);
            if (root is not null && !Directory.EnumerateFileSystemEntries(root.FullName).Any())
                Directory.Delete(root.FullName);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
