using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using vKOROBKU.Protocol;

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
            var jobLine = await reader.ReadLineAsync();
            var job = jobLine is null ? null : JsonSerializer.Deserialize<WorkerJob>(jobLine, JsonOptions);
            if (job is null)
            {
                await SendAsync(writer, new WorkerMessage("error", "Не получено задание."));
                return 3;
            }

            using var cancellation = new CancellationTokenSource();
            var monitorTask = MonitorCommandsAsync(reader, cancellation);
            try
            {
                await ExecuteAsync(job, writer, cancellation.Token);
                return 0;
            }
            catch (OperationCanceledException)
            {
                await SendAsync(writer, new WorkerMessage("cancelled", "Операция остановлена. Уже обработанные файлы остаются в корректном состоянии."));
                return 4;
            }
            catch (Exception exception)
            {
                await SendAsync(writer, new WorkerMessage("error", exception.Message));
                return 5;
            }
            finally
            {
                cancellation.Cancel();
                try { await monitorTask; } catch { }
            }
        }
        catch
        {
            return 6;
        }
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
        var (pendingFiles, skippedBytes, skippedFiles) = PartitionPendingFiles(
            files, file => IsAlreadyProcessed(file, job), cancellationToken);
        long processedBytes = skippedBytes;
        var processedFiles = skippedFiles;
        await SendAsync(writer, new WorkerMessage(
            "progress",
            skippedFiles > 0
                ? $"Пропущено уже обработанных файлов: {skippedFiles:N0} — продолжаем с места остановки"
                : "Подготовка завершена",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            PhysicalBefore: physicalBefore));

        var batches = BatchPlanner.CreateBatches(pendingFiles);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = await RunCompactAsync(batch, job, cancellationToken);

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
            "status", "Проверяем результат обработки…",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            PhysicalBefore: physicalBefore));
        var errorCount = CompressionResultVerifier.CountErrors(files, job, cancellationToken);
        var physicalAfter = MeasurePhysicalSize(files);
        await SendAsync(writer, new WorkerMessage(
            "completed",
            errorCount == 0 ? "Операция завершена" : "Операция завершена с пропущенными файлами",
            ProcessedBytes: processedBytes,
            TotalBytes: totalBytes,
            ProcessedFiles: processedFiles,
            TotalFiles: files.Count,
            ErrorCount: errorCount,
            PhysicalBefore: physicalBefore,
            PhysicalAfter: physicalAfter));
    }

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
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        var excluded = (info.Attributes & (FileAttributes.Encrypted | FileAttributes.ReparsePoint | FileAttributes.SparseFile)) != 0;
                        if (!excluded && info.Length > 0)
                            result.Add(new WorkerFile(path, info.Length));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(child);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
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
        {
            Marshal.SetLastPInvokeError(0);
            uint high;
            var low = GetCompressedFileSizeW(file.Path, out high);
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
            {
                total += file.Length;
                continue;
            }
            total += checked((long)(((ulong)high << 32) | low));
        }
        return total;
    }

    private static async Task MonitorCommandsAsync(StreamReader reader, CancellationTokenSource cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellation.Token);
            if (line is null)
                break;
            var command = JsonSerializer.Deserialize<WorkerCommand>(line, JsonOptions);
            if (command?.Type == "cancel")
            {
                cancellation.Cancel();
                break;
            }
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

}
