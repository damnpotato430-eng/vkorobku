using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

public sealed class CompressionWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private StreamWriter? _activeWriter;

    public async Task<WorkerMessage> ExecuteAsync(
        WorkerJob job,
        IProgress<WorkerMessage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var workerPath = Path.Combine(AppContext.BaseDirectory, "vKOROBKU.Worker.exe");
        if (!File.Exists(workerPath))
            throw new FileNotFoundException("Не найден системный модуль vKOROBKU.Worker.exe.", workerPath);

        var pipeName = $"vkorobku-{Guid.NewGuid():N}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenFile = WorkerTokenFile.Create(token);
        try
        {
            using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            Process? worker;
            try
            {
                worker = Process.Start(new ProcessStartInfo
                {
                    FileName = workerPath,
                    Arguments = $"--pipe {pipeName} --token-file \"{tokenFile}\"",
                    WorkingDirectory = AppContext.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                throw new OperationCanceledException("Запрос прав администратора отменён.", exception);
            }

            if (worker is null)
                throw new InvalidOperationException("Не удалось запустить системный модуль.");

            using (worker)
            {
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await pipe.WaitForConnectionAsync(connectionTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Системный модуль не подключился за 30 секунд.");
                }

                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                _activeWriter = writer;
                try
                {
                    var helloLine = await reader.ReadLineAsync(cancellationToken);
                    var hello = helloLine is null ? null : JsonSerializer.Deserialize<WorkerMessage>(helloLine, JsonOptions);
                    if (hello?.Type != "hello" || !CryptographicOperations.FixedTimeEquals(
                            Encoding.ASCII.GetBytes(hello.Token ?? string.Empty), Encoding.ASCII.GetBytes(token)))
                        throw new InvalidOperationException("Не удалось подтвердить подлинность системного модуля.");

                    await WriteAsync(writer, JsonSerializer.Serialize(job, JsonOptions), cancellationToken);
                    while (true)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);
                        if (line is null)
                            throw new IOException("Системный модуль неожиданно завершил соединение.");
                        var message = JsonSerializer.Deserialize<WorkerMessage>(line, JsonOptions)
                            ?? throw new InvalidDataException("Получен некорректный ответ системного модуля.");
                        progress?.Report(message);

                        if (message.Type == "completed" || message.Type == "cancelled")
                            return message;
                        if (message.Type == "error")
                            throw new InvalidOperationException(message.Text ?? "Ошибка системного модуля.");
                    }
                }
                finally
                {
                    _activeWriter = null;
                    if (!worker.HasExited)
                    {
                        try { await worker.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch { }
                    }
                }
            }
        }
        finally
        {
            WorkerTokenFile.Delete(tokenFile);
        }
    }

    public async Task CancelAsync()
    {
        var writer = _activeWriter;
        if (writer is null)
            return;
        try
        {
            await WriteAsync(writer, JsonSerializer.Serialize(new WorkerCommand("cancel"), JsonOptions), CancellationToken.None);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task WriteAsync(StreamWriter writer, string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
