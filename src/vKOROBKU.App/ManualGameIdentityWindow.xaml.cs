using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace vKOROBKU.App;

public partial class ManualGameIdentityWindow : Window
{
    public ManualGameIdentityWindow(string currentName)
    {
        InitializeComponent();
        GameNameBox.Text = currentName;
        GameNameBox.SelectAll();
        SourceInitialized += EnableDarkTitleBar;
        Loaded += (_, _) => GameNameBox.Focus();
    }

    public string GameName => GameNameBox.Text.Trim();

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (GameName.Length < 2)
        {
            MessageBox.Show(this, "Введите название игры.", "vKOROBKU", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
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
