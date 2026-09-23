using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

/// <summary>A scrollable review for queues of any size. The action buttons stay
/// visible, and Escape/Enter initially cancel rather than approve file changes.</summary>
public sealed class OperationConfirmationWindow : Window
{
    public OperationConfirmationWindow(string title, string message, string confirmLabel, int scalePercent)
    {
        Title = title;
        Width = 680;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("BackgroundBrush");
        Foreground = (Brush)FindResource("TextBrush");
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14,
                Margin = new Thickness(0, 0, 12, 0)
            }
        });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var confirm = new Button { Content = confirmLabel, Margin = new Thickness(0, 0, 12, 8) };
        confirm.Click += (_, _) => DialogResult = true;
        var cancel = new Button
        {
            Content = Strings.Settings_Cancel, IsCancel = true, IsDefault = true,
            Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 0, 8)
        };
        cancel.Click += (_, _) => DialogResult = false;
        Loaded += (_, _) => cancel.Focus();
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        Content = root;
        UiScale.Apply(this, root, scalePercent, new Size(420, 320));
    }

    public static bool Confirm(string title, string message, string confirmLabel, int scalePercent) =>
        new OperationConfirmationWindow(title, message, confirmLabel, scalePercent)
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog() == true;
}
