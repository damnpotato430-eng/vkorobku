using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<string> _userExtensions;

    public SettingsWindow(UserPreferences preferences)
    {
        InitializeComponent();
        Result = preferences;
        WatcherEnabledBox.IsChecked = preferences.WatcherEnabled;
        DecayBox.Text = preferences.DecayThresholdPercent.ToString(CultureInfo.CurrentCulture);
        SavingsBox.Text = preferences.MinimumSavingsMb.ToString(CultureInfo.CurrentCulture);
        SkipEnabledBox.IsChecked = preferences.SkipNonCompressable;
        _userExtensions = new ObservableCollection<string>(preferences.UserSkipExtensions ?? []);
        UserExtensionsList.ItemsSource = _userExtensions;
        DefaultListText.Text =
            $"Стандартный список ({CompressionSkipList.DefaultExtensions.Count} расширений): " +
            string.Join(" ", CompressionSkipList.DefaultExtensions);
    }

    public UserPreferences Result { get; private set; }

    private void AddExtensionClick(object sender, RoutedEventArgs e)
    {
        if (!CompressionSkipList.TryNormalizeExtension(NewExtensionBox.Text, out var extension))
        {
            MessageBox.Show(this, "Расширение указывается в формате «.ext» — точка и до 15 символов без пробелов.",
                "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CompressionSkipList.DefaultExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, $"Расширение {extension} уже входит в стандартный список.",
                "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_userExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, $"Расширение {extension} уже добавлено.",
                "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _userExtensions.Add(extension);
        NewExtensionBox.Clear();
    }

    private void RemoveExtensionClick(object sender, RoutedEventArgs e)
    {
        if (UserExtensionsList.SelectedItem is string selected)
            _userExtensions.Remove(selected);
    }

    private void ResetExtensionsClick(object sender, RoutedEventArgs e)
    {
        if (_userExtensions.Count == 0)
            return;
        var confirmation = MessageBox.Show(this,
            "Удалить все добавленные вручную расширения и вернуться к стандартному списку?",
            "Настройки", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation == MessageBoxResult.Yes)
            _userExtensions.Clear();
    }

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
            MinimumSavingsMb = savings,
            SkipNonCompressable = SkipEnabledBox.IsChecked == true,
            UserSkipExtensions = _userExtensions.ToArray()
        };
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
