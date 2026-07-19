using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using vKOROBKU.App.Resources;
using vKOROBKU.Protocol;

namespace vKOROBKU.App.Services;

public sealed class CompressionWorkerClient
{
    private WorkerSession? _activeSession;

    /// <summary>Runs a single job in its own elevated session (one UAC prompt).</summary>
    public async Task<WorkerMessage> ExecuteAsync(
        WorkerJob job,
        IProgress<WorkerMessage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = await StartSessionAsync(cancellationToken);
        var result = await session.RunJobAsync(job, progress, cancellationToken);
        if (result.Type == "error")
            throw new InvalidOperationException(result.Text ?? Strings.Worker_Error);
        return result;
    }

    /// <summary>Starts one elevated worker (one UAC prompt) that can process a queue of
    /// jobs sequentially via <see cref="WorkerSession.RunJobAsync"/>.</summary>
    public async Task<WorkerSession> StartSessionAsync(CancellationToken cancellationToken = default)
    {
        var workerPath = Path.Combine(AppContext.BaseDirectory, "vKOROBKU.Worker.exe");
        if (!File.Exists(workerPath))
            throw new FileNotFoundException(Strings.Worker_NotFound, workerPath);

        var session = await WorkerSession.StartAsync(workerPath, cancellationToken);
        _activeSession = session;
        session.Closed += () =>
        {
            if (ReferenceEquals(_activeSession, session))
                _activeSession = null;
        };
        return session;
    }

    public Task CancelAsync() => _activeSession?.CancelCurrentJobAsync() ?? Task.CompletedTask;
}

/// <summary>One elevated worker process. Jobs run strictly one at a time; the session
/// ends with a shutdown command on dispose (or by the pipe closing).</summary>
public sealed class WorkerSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly Process _worker;
    private readonly string _tokenFile;
    private bool _disposed;

    public event Action? Closed;

    private WorkerSession(NamedPipeServerStream pipe, StreamReader reader, StreamWriter writer, Process worker, string tokenFile)
    {
        _pipe = pipe;
        _reader = reader;
        _writer = writer;
        _worker = worker;
        _tokenFile = tokenFile;
    }

    internal static async Task<WorkerSession> StartAsync(string workerPath, CancellationToken cancellationToken)
    {
        var pipeName = $"vkorobku-{Guid.NewGuid():N}";
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenFile = WorkerTokenFile.Create(token);
        NamedPipeServerStream? pipe = null;
        Process? worker = null;
        try
        {
            pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

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
                throw new OperationCanceledException(Strings.Worker_UacCancelled, exception);
            }

            if (worker is null)
                throw new InvalidOperationException(Strings.Worker_StartFailed);

            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var connectTask = pipe.WaitForConnectionAsync(connectionTimeout.Token);
            var workerExitTask = WaitForWorkerExitAsync(worker, connectionTimeout.Token);
            try
            {
                var first = await Task.WhenAny(connectTask, workerExitTask);
                if (first == workerExitTask && !connectTask.IsCompleted)
                {
                    connectionTimeout.Cancel();
                    try { await connectTask; }
                    catch (OperationCanceledException) { }
                    throw new InvalidOperationException(DescribeEarlyWorkerExit(worker.ExitCode));
                }
                await connectTask;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(Strings.Worker_ConnectTimeout);
            }

            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

            var helloLine = await reader.ReadLineAsync(cancellationToken);
            var hello = helloLine is null ? null : JsonSerializer.Deserialize<WorkerMessage>(helloLine, JsonOptions);
            if (hello?.Type != "hello" || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(hello.Token ?? string.Empty), Encoding.ASCII.GetBytes(token)))
                throw new InvalidOperationException(Strings.Worker_AuthFailed);

            AppLog.Info("Сессия воркера: запущена и подтверждена");
            return new WorkerSession(pipe, reader, writer, worker, tokenFile);
        }
        catch
        {
            worker?.Dispose();
            pipe?.Dispose();
            WorkerTokenFile.Delete(tokenFile);
            throw;
        }
    }

    /// <summary>Sends one job and pumps messages until it reports completed, cancelled
    /// or error. The error outcome is returned, not thrown — a queue decides for itself
    /// whether a failed game stops the run.</summary>
    public async Task<WorkerMessage> RunJobAsync(
        WorkerJob job,
        IProgress<WorkerMessage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AppLog.Info($"Сессия воркера: задание {job.Operation} — {job.RootPath}");
        await WriteAsync(JsonSerializer.Serialize(job, JsonOptions), cancellationToken);
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                AppLog.Error("Сессия воркера: соединение неожиданно закрылось", new IOException(job.RootPath));
                throw new IOException(Strings.Worker_ConnectionLost);
            }
            var message = JsonSerializer.Deserialize<WorkerMessage>(line, JsonOptions)
                ?? throw new InvalidDataException(Strings.Worker_BadResponse);
            progress?.Report(message);

            if (message.Type is "completed" or "cancelled" or "error")
            {
                AppLog.Info($"Сессия воркера: задание завершилось — {message.Type}");
                return message;
            }
        }
    }

    public async Task CancelCurrentJobAsync()
    {
        if (_disposed)
            return;
        try
        {
            AppLog.Info("Сессия воркера: отправлена команда отмены текущего задания");
            await WriteAsync(JsonSerializer.Serialize(new WorkerCommand("cancel"), JsonOptions), CancellationToken.None);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        AppLog.Info("Сессия воркера: завершение (shutdown)");
        try
        {
            await WriteAsync(JsonSerializer.Serialize(new WorkerCommand("shutdown"), JsonOptions), CancellationToken.None);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }

        if (!_worker.HasExited)
        {
            try { await _worker.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
        }
        _worker.Dispose();
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
        WorkerTokenFile.Delete(_tokenFile);
        Closed?.Invoke();
    }

    private static async Task WaitForWorkerExitAsync(Process worker, CancellationToken cancellationToken)
    {
        try { await worker.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    // Exit code 2 means the worker could not read the one-time token file. Its ACL and
    // location are bound to the invoking user, so this happens when UAC is confirmed
    // by a different Windows account.
    private static string DescribeEarlyWorkerExit(int exitCode) => exitCode switch
    {
        2 => Strings.Worker_TokenExit,
        _ => string.Format(Strings.Worker_EarlyExit, exitCode)
    };

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
