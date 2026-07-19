using vKOROBKU.App.Resources;
using System.Diagnostics;
using vKOROBKU.App.Models;

namespace vKOROBKU.App.Services;

public sealed class CompactProcessService
{
    public async Task CompressAsync(string directory, CompressionAlgorithm algorithm, CancellationToken cancellationToken)
    {
        var algorithmName = algorithm switch
        {
            CompressionAlgorithm.Xpress4K => "XPRESS4K",
            CompressionAlgorithm.Xpress8K => "XPRESS8K",
            CompressionAlgorithm.Xpress16K => "XPRESS16K",
            CompressionAlgorithm.Lzx => "LZX",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };

        await RunAsync(directory, ["/C", "/S", "/I", "/F", $"/EXE:{algorithmName}", "*"], cancellationToken);
    }

    public async Task DecompressAsync(string directory, CancellationToken cancellationToken)
    {
        // compact.exe /U /EXE removes the XPRESS/LZX (WOF) backing applied by CompressAsync;
        // plain /U removes only NTFS compression and does not touch WOF files.
        await RunAsync(directory, ["/U", "/EXE", "/S", "/I", "/F", "*"], cancellationToken);
        await RunAsync(directory, ["/U", "/S", "/I", "/F", "*"], cancellationToken);
    }

    private static async Task RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "compact.exe"),
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(Strings.Compact_StartFailed);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                string.Format(Strings.Compact_ExitCode, process.ExitCode, $"{error}\n{output}".Trim()));
    }
}
