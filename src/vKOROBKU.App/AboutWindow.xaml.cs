using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using vKOROBKU.App.Resources;

namespace vKOROBKU.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        VersionText.Text = string.Format(Strings.About_Version, version.ToString(3));
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        TryOpen(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OpenLogsClick(object sender, RoutedEventArgs e)
    {
        var logs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vKOROBKU", "logs");
        try { Directory.CreateDirectory(logs); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        TryOpen(logs);
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void TryOpen(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Win32Exception) { }
        catch (IOException) { }
    }
}
