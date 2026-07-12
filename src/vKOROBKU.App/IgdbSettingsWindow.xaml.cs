using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using vKOROBKU.App.Models;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

public partial class IgdbSettingsWindow : Window
{
    private readonly IgdbCredentialStore _store = new();

    public IgdbSettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += EnableDarkTitleBar;
        var credentials = _store.Load();
        if (credentials is not null)
        {
            ClientIdBox.Text = credentials.ClientId;
            ClientSecretBox.Password = credentials.ClientSecret;
        }
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var credentials = new IgdbCredentials(ClientIdBox.Text.Trim(), ClientSecretBox.Password);
        if (!credentials.IsValid)
        {
            MessageBox.Show(this, "Укажите Client ID и Client Secret.", "IGDB", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _store.Save(credentials);
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void EnableDarkTitleBar(object? sender, EventArgs e)
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
