using System.Windows;
using System.Windows.Media;

namespace vKOROBKU.App.Services;

/// <summary>Applies the user's extra zoom on top of the Windows display scaling.
/// A LayoutTransform is used rather than a RenderTransform so the scaled size takes
/// part in layout: panels re-flow at the new size instead of being clipped, and WPF
/// keeps rendering vector-crisp at any factor.</summary>
public static class UiScale
{
    public static readonly IReadOnlyList<int> SupportedPercents = [100, 110, 125, 150];

    public static int Normalize(int percent) =>
        SupportedPercents.Contains(percent) ? percent : 100;

    public static void Apply(Window window, FrameworkElement root, int percent, Size baseMinimumSize)
    {
        var scale = Normalize(percent) / 100d;
        root.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);

        // The scaled content needs proportionally more room, so the minimum window
        // size grows with it — otherwise the user could shrink the window until the
        // layout is unusable. It is capped to the working area because a minimum
        // larger than the screen would make the window impossible to fit.
        var workingArea = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(baseMinimumSize.Width * scale, workingArea.Width);
        window.MinHeight = Math.Min(baseMinimumSize.Height * scale, workingArea.Height);
        if (window.Width < window.MinWidth)
            window.Width = window.MinWidth;
        if (window.Height < window.MinHeight)
            window.Height = window.MinHeight;
    }
}
