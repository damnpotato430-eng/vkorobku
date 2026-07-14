using System.Text;

namespace vKOROBKU.App.Services;

/// <summary>Lightweight file log for field diagnostics; never throws into callers.</summary>
public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "vKOROBKU", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception}");

    public static void CleanupOldLogs(int keepDays = 7)
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
                return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "app-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never break the application.
        }
    }
}
