using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;
using vKOROBKU.App.ViewModels;

namespace vKOROBKU.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _closeApproved;

    // The designer sizes are the 100% baseline the scale multiplies from.
    private static readonly Size BaseMinimumSize = new(1040, 700);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.UiScaleChanged += ApplyUiScale;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ApplyUiScale(_viewModel.UiScalePercent);
    }

    private void ApplyUiScale(int percent) =>
        UiScale.Apply(this, RootGrid, percent, BaseMinimumSize);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        // Attribute 20 is supported by current Windows 10/11 builds; 19 covers older Windows 10.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    // Closing cannot be awaited, so the first pass cancels the close, stops the work
    // and then closes again — the flag keeps that second pass from asking twice.
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved || !_viewModel.HasWorkInProgress)
            return;

        e.Cancel = true;
        var prompt = _viewModel.HasFileChangingWorkInProgress
            ? Strings.Close_OperationPrompt
            : Strings.Close_AnalysisPrompt;
        var confirmation = MessageBox.Show(
            this, prompt, Strings.Close_Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _closeApproved = true;
        await _viewModel.StopAllWorkAsync();
        Close();
    }

    private void OnGameItemPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item)
            item.IsSelected = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
}
