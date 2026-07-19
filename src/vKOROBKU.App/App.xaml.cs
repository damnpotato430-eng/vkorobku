using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;

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
        ApplyLanguagePreference();
        base.OnStartup(e);
    }

    // Must run before the main window is created: XAML resolves the x:Static string
    // properties at construction time. "auto" keeps the OS UI culture — the resource
    // fallback then serves Russian to Russian systems and English to everything else.
    private static void ApplyLanguagePreference()
    {
        try
        {
            var language = new UserPreferencesStore().Load().Language;
            if (language is not ("ru" or "en"))
                return;
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { }
        catch (Exception exception)
        {
            AppLog.Error("Не удалось применить язык интерфейса", exception);
        }
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

    private int _unhandledUiExceptionCount;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.Handled = true;
        _unhandledUiExceptionCount++;
        if (_unhandledUiExceptionCount >= 3)
        {
            MessageBox.Show(
                $"{Strings.App_ErrorRepeated}\n\n{e.Exception.Message}",
                "vKOROBKU",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        MessageBox.Show(
            $"{Strings.App_ErrorContinues}\n\n{e.Exception.Message}",
            "vKOROBKU",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
