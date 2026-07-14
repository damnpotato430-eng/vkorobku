using System.Globalization;
using System.Windows;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(UserPreferences preferences)
    {
        InitializeComponent();
        Result = preferences;
        WatcherEnabledBox.IsChecked = preferences.WatcherEnabled;
        DecayBox.Text = preferences.DecayThresholdPercent.ToString(CultureInfo.CurrentCulture);
        SavingsBox.Text = preferences.MinimumSavingsMb.ToString(CultureInfo.CurrentCulture);
    }

    public UserPreferences Result { get; private set; }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DecayBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var decay) ||
            decay is < 1 or > 100)
        {
            MessageBox.Show(this, "Порог деградации — число от 1 до 100 процентов.",
                "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SavingsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var savings) ||
            savings < 1)
        {
            MessageBox.Show(this, "Минимальная выгода — целое число мегабайт, не меньше 1.",
                "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = Result with
        {
            WatcherEnabled = WatcherEnabledBox.IsChecked == true,
            DecayThresholdPercent = decay,
            MinimumSavingsMb = savings
        };
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
