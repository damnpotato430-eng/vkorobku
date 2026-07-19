using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

public partial class AboutWindow : Window
{
    private readonly UpdateCheckService _updateCheckService = new();

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

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;
        button.IsEnabled = false;
        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            var newer = await _updateCheckService.CheckForNewerReleaseAsync(current);
            if (newer is null)
            {
                MessageBox.Show(this, Strings.About_UpToDate,
                    Strings.About_UpdatesTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var open = MessageBox.Show(this,
                string.Format(Strings.About_UpdateAvailable, newer.Value.Version.ToString(3)),
                Strings.About_UpdatesTitle, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
                TryOpen(newer.Value.Url);
        }
        finally
        {
            button.IsEnabled = true;
        }
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
