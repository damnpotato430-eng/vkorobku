using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace vKOROBKU.App;

public partial class App : Application
{
    private const int SwRestore = 9;

    // Kept alive for the process lifetime; the OS releases the mutex when the process exits.
    private static Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\vKOROBKU-single-instance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateRunningInstance();
            Shutdown(0);
            return;
        }
        base.OnStartup(e);
    }

    private static void ActivateRunningInstance()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                {
                    if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero)
                        continue;
                    _ = ShowWindow(process.MainWindowHandle, SwRestore);
                    _ = SetForegroundWindow(process.MainWindowHandle);
                    return;
                }
            }
        }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        MessageBox.Show(
            $"Произошла непредвиденная ошибка. Отчёт сохранён в папке logs.\n\n{e.Exception.Message}",
            "vKOROBKU",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "vKOROBKU", "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var content = new StringBuilder()
                .AppendLine($"vKOROBKU crash at {DateTimeOffset.Now:O}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine($".NET: {Environment.Version}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();
            File.WriteAllText(path, content);
        }
        catch
        {
            // Crash logging must never hide the original failure.
        }
    }
}
