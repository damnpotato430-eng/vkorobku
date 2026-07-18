extern alias worker;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using vKOROBKU.Protocol;

namespace vKOROBKU.Tests;

/// <summary>Drives a real worker process through the pipe protocol (no elevation is
/// required when the worker is started directly), covering the queue session:
/// several jobs per launch and a cancel that must end only the current job.</summary>
public sealed class WorkerQueueSessionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Session_RunsSecondJobAfterCancellingFirst()
    {
        // The apphost carries a requireAdministrator manifest, so the worker runs
        // through the dotnet muxer instead — the protocol under test is the same.
        var workerDll = Path.Combine(AppContext.BaseDirectory, "vKOROBKU.Worker.dll");
        Assert.True(File.Exists(workerDll), $"Не найден {workerDll}");
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var dotnetPath = string.IsNullOrWhiteSpace(dotnetRoot)
            ? "dotnet"
            : Path.Combine(dotnetRoot, "dotnet.exe");

        var bigFolder = CreateGameFolder(fileCount: 300, fileSize: 512 * 1024, random: true);
        var smallFolder = CreateGameFolder(fileCount: 4, fileSize: 64 * 1024, random: false);
        var tokenFile = CreateTokenFile(out var token);
        try
        {
            var pipeName = $"vkorobku-test-{Guid.NewGuid():N}";
            using var pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(workerDll);
            startInfo.ArgumentList.Add("--pipe");
            startInfo.ArgumentList.Add(pipeName);
            startInfo.ArgumentList.Add("--token-file");
            startInfo.ArgumentList.Add(tokenFile);
            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            try
            {
                await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

                var hello = await ReadMessageAsync(reader, TimeSpan.FromSeconds(15));
                Assert.Equal("hello", hello.Type);
                Assert.Equal(token, hello.Token);

                // Job 1: cancel as soon as compression is underway.
                await WriteLineAsync(writer, new WorkerJob(bigFolder, "compress", "LZX"));
                WorkerMessage message;
                do
                {
                    message = await ReadMessageAsync(reader, TimeSpan.FromSeconds(60));
                } while (message.Type is not ("progress" or "completed" or "cancelled" or "error"));

                if (message.Type == "progress")
                {
                    // Let compact.exe get properly underway so the cancel lands
                    // mid-run (the kill path), matching how a user presses skip.
                    await Task.Delay(1500);
                    await WriteLineAsync(writer, new WorkerCommand("cancel"));
                    do
                    {
                        message = await ReadMessageAsync(reader, TimeSpan.FromSeconds(60));
                    } while (message.Type is not ("completed" or "cancelled" or "error"));
                }
                Assert.NotEqual("error", message.Type);

                // Job 2 must run to completion in the same session — the regression
                // under test is the session stalling after a cancelled job.
                await WriteLineAsync(writer, new WorkerJob(smallFolder, "compress", "XPRESS4K"));
                do
                {
                    message = await ReadMessageAsync(reader, TimeSpan.FromSeconds(60));
                } while (message.Type is not ("completed" or "cancelled" or "error"));
                Assert.Equal("completed", message.Type);

                await WriteLineAsync(writer, new WorkerCommand("shutdown"));
                await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(0, process.ExitCode);
            }
            finally
            {
                try { if (process is { HasExited: false }) process.Kill(true); } catch { }
            }
        }
        finally
        {
            try { Directory.Delete(bigFolder, true); } catch { }
            try { Directory.Delete(smallFolder, true); } catch { }
            try { File.Delete(tokenFile); } catch { }
        }
    }

    private static async Task<WorkerMessage> ReadMessageAsync(StreamReader reader, TimeSpan timeout)
    {
        var line = await reader.ReadLineAsync().WaitAsync(timeout);
        Assert.NotNull(line);
        var message = JsonSerializer.Deserialize<WorkerMessage>(line, JsonOptions);
        Assert.NotNull(message);
        return message;
    }

    private static Task WriteLineAsync<T>(StreamWriter writer, T payload) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions));

    private static string CreateGameFolder(int fileCount, int fileSize, bool random)
    {
        var root = Path.Combine(Path.GetTempPath(), $"vkorobku-queue-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var generator = new Random(42);
        for (var index = 0; index < fileCount; index++)
        {
            var bytes = new byte[fileSize];
            if (random)
                generator.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(root, $"data{index:D4}.bin"), bytes);
        }
        return root;
    }

    private static string CreateTokenFile(out string token)
    {
        token = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "WorkerAuth");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.token");
        File.WriteAllText(path, token, new UTF8Encoding(false));
        return path;
    }
}
