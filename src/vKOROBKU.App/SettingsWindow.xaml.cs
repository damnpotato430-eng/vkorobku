using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using vKOROBKU.App.Resources;
using vKOROBKU.App.Services;

namespace vKOROBKU.App;

public partial class SettingsWindow : Window
{
    // Value = what lands in preferences; the display names of the concrete languages
    // are deliberately not localized — each one is written in its own language.
    private static readonly (string Value, Func<string> Display)[] LanguageOptions =
    [
        ("auto", () => Strings.Settings_LanguageAuto),
        ("ru", () => "Русский"),
        ("en", () => "English")
    ];

    private readonly ObservableCollection<string> _userExtensions;

    public SettingsWindow(UserPreferences preferences)
    {
        InitializeComponent();
        Result = preferences;
        foreach (var option in LanguageOptions)
            LanguageBox.Items.Add(option.Display());
        var languageIndex = Array.FindIndex(LanguageOptions, option => option.Value == preferences.Language);
        LanguageBox.SelectedIndex = languageIndex >= 0 ? languageIndex : 0;
        WatcherEnabledBox.IsChecked = preferences.WatcherEnabled;
        DecayBox.Text = preferences.DecayThresholdPercent.ToString(CultureInfo.CurrentCulture);
        SavingsBox.Text = preferences.MinimumSavingsMb.ToString(CultureInfo.CurrentCulture);
        SkipEnabledBox.IsChecked = preferences.SkipNonCompressable;
        _userExtensions = new ObservableCollection<string>(preferences.UserSkipExtensions ?? []);
        UserExtensionsList.ItemsSource = _userExtensions;
        DefaultListText.Text = string.Format(
            Strings.Settings_DefaultListFormat,
            CompressionSkipList.DefaultExtensions.Count,
            string.Join(" ", CompressionSkipList.DefaultExtensions));
    }

    public UserPreferences Result { get; private set; }

    private void AddExtensionClick(object sender, RoutedEventArgs e)
    {
        if (!CompressionSkipList.TryNormalizeExtension(NewExtensionBox.Text, out var extension))
        {
            MessageBox.Show(this, Strings.Settings_ExtensionFormatError,
                Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CompressionSkipList.DefaultExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, string.Format(Strings.Settings_ExtensionInDefault, extension),
                Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_userExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, string.Format(Strings.Settings_ExtensionExists, extension),
                Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Information);
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
            Strings.Settings_ResetConfirm,
            Strings.Settings_Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation == MessageBoxResult.Yes)
            _userExtensions.Clear();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DecayBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var decay) ||
            decay is < 1 or > 100)
        {
            MessageBox.Show(this, Strings.Settings_DecayError,
                Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(SavingsBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var savings) ||
            savings < 1)
        {
            MessageBox.Show(this, Strings.Settings_SavingsError,
                Strings.Settings_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var language = LanguageBox.SelectedIndex >= 0 && LanguageBox.SelectedIndex < LanguageOptions.Length
            ? LanguageOptions[LanguageBox.SelectedIndex].Value
            : "auto";
        Result = Result with
        {
            WatcherEnabled = WatcherEnabledBox.IsChecked == true,
            DecayThresholdPercent = decay,
            MinimumSavingsMb = savings,
            SkipNonCompressable = SkipEnabledBox.IsChecked == true,
            UserSkipExtensions = _userExtensions.ToArray(),
            Language = language
        };
        DialogResult = true;
    }

    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
