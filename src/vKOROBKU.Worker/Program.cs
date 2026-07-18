using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using vKOROBKU.Protocol;
using vKOROBKU.Shared;

namespace vKOROBKU.Worker;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        var pipeName = ReadArgument(args, "--pipe");
        var expectedToken = ReadAndDeleteToken(args);
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(expectedToken))
            return 2;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(15_000);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

            await SendAsync(writer, new WorkerMessage("hello", Token: expectedToken));

            // One elevated launch serves a whole queue of jobs: the app sends the next
            // job only after the previous one reported completed/cancelled/error, and
            // ends the session with a shutdown command (or by closing the pipe). A
            // single pump feeds every incoming line into the channel so job reads and
            // in-flight cancel commands never race for the pipe reader.
            var inbox = Channel.CreateUnbounded<string>();
            _ = PumpLinesAsync(reader, inbox.Writer);
            var processedAnyJob = false;

            while (true)
            {
                string? line;
                try { line = await inbox.Reader.ReadAsync(); }
                catch (ChannelClosedException) { return processedAnyJob ? 0 : 3; }

                var command = TryDeserialize<WorkerCommand>(line);
                if (command?.Type == "shutdown")
                    return 0;
                if (command?.Type is not null)
                    continue;

                var job = TryDeserialize<WorkerJob>(line);
                if (job is null)
                {
                    await SendAsync(writer, new WorkerMessage("error", "Не получено задание."));
                    return 3;
                }

                processedAnyJob = true;
                using var cancellation = new CancellationTokenSource();
                var monitorTask = MonitorCommandsAsync(inbox.Reader, cancellation);
                try
                {
                    await ExecuteAsync(job, writer, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    await SendAsync(writer, new WorkerMessage("cancelled", "Операция остановлена. Уже обработанные файлы остаются в корректном состоянии."));
                }
                catch (Exception exception)
                {
                    // A failed job ends only that job — the queue decides whether to
                    // continue with the next game or shut the session down.
                    await SendAsync(writer, new WorkerMessage("error", exception.Message));
                }
                finally
                {
                    cancellation.Cancel();
                    try { await monitorTask; } catch { }
                }
            }
        }
        catch
        {
            return 6;
        }
    }

    private static async Task PumpLinesAsync(StreamReader reader, ChannelWriter<string> inbox)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
                await inbox.WriteAsync(line);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            inbox.Complete();
        }
    }

    private static T? TryDeserialize<T>(string line) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(line, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static async Task ExecuteAsync(WorkerJob job, StreamWriter writer, CancellationToken cancellationToken)
    {
        var rootPath = ValidateJob(job);
        EnsureGameIsNotRunning(rootPath);

        await SendAsync(writer, new WorkerMessage("status", "Сканируем файлы игры…"));
        var files = EnumerateFiles(rootPath, cancellationToken);
        if (files.Count == 0)
            throw new InvalidOperationException("В каталоге нет доступных файлов для обработки.");

        var totalBytes = files.Sum(file => file.Length);
        var physicalBefore = MeasurePhysicalSize(files);

        await SendAsync(writer, new WorkerMessage("status", "Проверяем, какие файлы уже обработаны…"));
        var skipExtensions = job.Operation == "compress" && job.SkipExtensions is { Length: > 0 }
            ? new HashSet<string>(job.SkipExtensions, StringComparer.OrdinalIgnoreCase)
            : null;
        var clusterSize = skipExtensions is null
            ? 0
            : VolumeInfo.GetClusterSize(Path.GetPathRoot(rootPath) ?? string.Empty);
        var skipListed = new List<WorkerFile>();
        IReadOnlyList<WorkerFile> candidates = files;
        if (skipExtensions is not null)
        {
            var compressible = new List<WorkerFile>(files.Count);
            foreach (var file in files)
            {
                if (IsSkipListed(file, skipExtensions, clusterSize))
                    skipListed.Add(file);
                else
                    compressible.Add(file);
            }
            candidates = compressible;
        }

        var (pendingFiles, resumeSkippedBytes, resumeSkippedFiles) = PartitionPendingFiles(
            candidates, file => IsAlreadyProcessed(file, job), cancellationToken);
        var skipListedBytes = skipListed.Sum(file => file.Length);
        long processedBytes = resumeSkippedBytes + skipListedBytes;
        var processedFiles = resumeSkippedFiles + skipListed.Count;
        var preparationNotes = new List<string>();
        if (resumeSkippedFiles > 0)
            preparationNotes.Add($"уже обработано: {resumeSkippedFiles:N0}");
        if (skipListed.Count > 0)
            preparationNotes.Add($"пропущено несжимаемых: {skipListed.Count:N0} ({FormatSize(skipListedBytes)})");
        await SendAsync(writer, new WorkerMessage(
            "progress",
            preparationNotes.Count > 0
                ? $"Подготовка завершена — {string.Join(" · ", preparationNotes)}"
                : "Подготовка завершена",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            PhysicalBefore: physicalBefore));

        var batches = BatchPlanner.CreateBatches(pendingFiles);
        var failedBatches = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exitCode = await RunCompactAsync(batch, job, cancellationToken);
            if (exitCode != 0)
            {
                failedBatches++;
                if (failedBatches == 1)
                    await SendAsync(writer, new WorkerMessage(
                        "status",
                        $"compact.exe сообщил об ошибке (код {exitCode}). Обработка продолжается, пропущенные файлы будут учтены при проверке."));
            }

            processedBytes += batch.Sum(file => file.Length);
            processedFiles += batch.Count;
            await SendAsync(writer, new WorkerMessage(
                "progress",
                job.Operation == "compress" ? "Сжимаем файлы…" : "Распаковываем файлы…",
                ProcessedBytes: processedBytes,
                TotalBytes: totalBytes,
                ProcessedFiles: processedFiles,
                TotalFiles: files.Count,
                PhysicalBefore: physicalBefore));
        }

        await SendAsync(writer, new WorkerMessage(
            "status",
            failedBatches == 0
                ? "Проверяем результат обработки…"
                : $"Проверяем результат обработки (пакетов с ошибками: {failedBatches})…",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            PhysicalBefore: physicalBefore));
        // Skip-listed files are uncompressed by design, so they are verified neither
        // as compressed nor as errors.
        var (errorCount, errorBytes) = CompressionResultVerifier.CountErrors(candidates, job, cancellationToken);
        var physicalAfter = MeasurePhysicalSize(files);
        var skipListedPhysical = skipListed.Count == 0 ? 0 : MeasurePhysicalSize(skipListed);
        await SendAsync(writer, new WorkerMessage(
            "completed",
            errorCount == 0 ? "Операция завершена" : "Операция завершена с пропущенными файлами",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            ErrorCount: errorCount,
            ErrorBytes: errorBytes,
            SkipListedFiles: skipListed.Count,
            SkipListedBytes: skipListedBytes,
            SkipListedPhysicalBytes: skipListedPhysical,
            PhysicalBefore: physicalBefore,
            PhysicalAfter: physicalAfter));
    }

    internal static bool IsSkipListed(WorkerFile file, HashSet<string> skipExtensions, long clusterSize) =>
        file.Length <= clusterSize || skipExtensions.Contains(Path.GetExtension(file.Path));

    private static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.#} ГБ"
        : bytes >= 1024L * 1024 ? $"{bytes / 1024d / 1024:0.#} МБ"
        : $"{Math.Max(1, bytes / 1024):N0} КБ";

    private static string ValidateJob(WorkerJob job)
    {
        if (job.Operation is not ("compress" or "decompress"))
            throw new InvalidOperationException("Неизвестная операция.");
        if (job.Operation == "compress" && job.Algorithm is not ("XPRESS4K" or "XPRESS8K" or "XPRESS16K" or "LZX"))
            throw new InvalidOperationException("Неизвестный алгоритм сжатия.");
        if (string.IsNullOrWhiteSpace(job.RootPath) || !Path.IsPathFullyQualified(job.RootPath))
            throw new InvalidOperationException("Некорректный путь игры.");

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.RootPath));
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException("Каталог игры не найден.");
        if (string.Equals(rootPath, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(rootPath)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя обрабатывать корень диска.");
        if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Корневой каталог игры является ссылкой или junction.");

        var drive = new DriveInfo(Path.GetPathRoot(rootPath)!);
        if (!drive.IsReady || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Сжатие XPRESS/LZX доступно только на NTFS.");
        return rootPath;
    }

    private static void EnsureGameIsNotRunning(string rootPath)
    {
        var prefix = rootPath + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is not null && executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Сначала закройте процесс игры: {process.ProcessName}.exe");
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException exception) when (!exception.Message.StartsWith("Сначала закройте", StringComparison.Ordinal)) { }
            }
        }
    }

    private static List<WorkerFile> EnumerateFiles(string rootPath, CancellationToken cancellationToken)
    {
        var result = new List<WorkerFile>();
        FileSystemWalker.Walk(rootPath, info =>
        {
            var excluded = (info.Attributes & (FileAttributes.Encrypted | FileAttributes.ReparsePoint | FileAttributes.SparseFile)) != 0;
            if (!excluded && info.Length > 0)
                result.Add(new WorkerFile(info.FullName, info.Length));
        }, cancellationToken);
        return result;
    }

    // A file is either fully converted or untouched at the NTFS level, so an interrupted
    // operation resumes by skipping files that are already in the target state.
    internal static (List<WorkerFile> Pending, long SkippedBytes, int SkippedFiles) PartitionPendingFiles(
        IReadOnlyList<WorkerFile> files,
        Func<WorkerFile, bool> isAlreadyProcessed,
        CancellationToken cancellationToken)
    {
        var pending = new List<WorkerFile>(files.Count);
        long skippedBytes = 0;
        var skippedFiles = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isAlreadyProcessed(file))
            {
                skippedBytes += file.Length;
                skippedFiles++;
            }
            else
            {
                pending.Add(file);
            }
        }
        return (pending, skippedBytes, skippedFiles);
    }

    private static bool IsAlreadyProcessed(WorkerFile file, WorkerJob job)
    {
        if (job.Operation == "compress")
            return CompressionResultVerifier.TryGetWofAlgorithm(file.Path, out var algorithm) &&
                   algorithm == CompressionResultVerifier.ParseAlgorithm(job.Algorithm);

        try
        {
            return (File.GetAttributes(file.Path) & FileAttributes.Compressed) == 0 &&
                   !CompressionResultVerifier.TryGetWofAlgorithm(file.Path, out _);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // compact.exe /U without /EXE removes only NTFS compression, while /U /EXE removes
    // only the XPRESS/LZX (WOF) backing, so a full decompress needs both passes.
    internal static IReadOnlyList<string[]> CreateCompactPasses(WorkerJob job)
    {
        if (job.Operation == "compress")
            return [["/C", "/I", "/F", "/Q", $"/EXE:{job.Algorithm}"]];

        return
        [
            ["/U", "/EXE", "/I", "/F", "/Q"],
            ["/U", "/I", "/F", "/Q"]
        ];
    }

    private static async Task<int> RunCompactAsync(IReadOnlyList<WorkerFile> files, WorkerJob job, CancellationToken cancellationToken)
    {
        var exitCode = 0;
        foreach (var passSwitches in CreateCompactPasses(job))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "compact.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var switchArgument in passSwitches)
                startInfo.ArgumentList.Add(switchArgument);
            foreach (var file in files)
                startInfo.ArgumentList.Add(file.Path);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить compact.exe.");
            var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The kill happens here, not in a token registration: registration
                // callbacks run after the awaiter's own registration in LIFO order,
                // so the old pattern could dispose the registration before the kill
                // ran and leave an orphan compact.exe compressing in the background.
                try { if (!process.HasExited) process.Kill(true); } catch { }
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
                throw;
            }
            _ = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0)
                exitCode = process.ExitCode;
        }
        return exitCode;
    }

    private static long MeasurePhysicalSize(IEnumerable<WorkerFile> files)
    {
        long total = 0;
        foreach (var file in files)
            total += PhysicalFileSize.GetOrDefault(file.Path, file.Length);
        return total;
    }

    // Watches the shared inbox for a cancel while a job runs. Cancelling the pending
    // ReadAsync does not consume an item, so the next job line stays in the channel
    // for the main loop. A closed channel means the app side is gone — the current
    // job is cancelled so the worker never keeps compressing without supervision.
    private static async Task MonitorCommandsAsync(ChannelReader<string> inbox, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var line = await inbox.ReadAsync(cancellation.Token);
                var command = TryDeserialize<WorkerCommand>(line);
                if (command?.Type is "cancel" or "shutdown")
                {
                    cancellation.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException)
        {
            cancellation.Cancel();
        }
    }

    private static Task SendAsync(StreamWriter writer, WorkerMessage message) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));

    private static string? ReadAndDeleteToken(string[] args)
    {
        var tokenFile = ReadArgument(args, "--token-file");
        if (string.IsNullOrWhiteSpace(tokenFile) || !Path.IsPathFullyQualified(tokenFile))
            return null;

        var fullPath = Path.GetFullPath(tokenFile);
        var allowedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "WorkerAuth") + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            if (directory is null ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return null;
            return File.ReadAllText(fullPath, Encoding.UTF8).Trim();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally
        {
            try { File.Delete(fullPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string? ReadArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

}
