using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace vKOROBKU.App;

public sealed class LocalImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Covers are displayed at ~224 DIPs; retain detail at high DPI without
            // decoding the full artwork for every card.
            image.DecodePixelWidth = 672;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
