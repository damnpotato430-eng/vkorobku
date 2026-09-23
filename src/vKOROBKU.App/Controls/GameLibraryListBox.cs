using System.Windows;
using System.Windows.Controls;

namespace vKOROBKU.App.Controls;

/// <summary>Game cards select by click or keyboard, never by dragging across cards.
/// Opening the inspector reflows tiles under the pointer while the button may still
/// be down. ListBox's default mouse capture would treat that as drag selection.</summary>
public sealed class GameLibraryListBox : ListBox
{
    protected override void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsMouseCapturedChanged(e);
        if (IsMouseCaptured && SelectionMode == SelectionMode.Single)
            ReleaseMouseCapture();
    }
}
