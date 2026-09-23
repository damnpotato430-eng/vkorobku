using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using vKOROBKU.App.Controls;

namespace vKOROBKU.Tests;

public sealed class VirtualizingTilePanelTests
{
    [Fact]
    public void ThousandGames_RealizesViewportOnly_AndHandlesScrollResizeAndRemoval() => OnSta(() =>
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 1000).Select(i => $"Game {i}"));
        var list = new ListBox
        {
            ItemsSource = items,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel)))
        };
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        list.Template = new ControlTemplate(typeof(ListBox)) { VisualTree = scroll };
        ScrollViewer.SetCanContentScroll(list, true);
        Layout(list, 600, 700);
        var panel = FindPanel(list);
        Assert.NotNull(panel);
        Assert.InRange(VisualTreeHelper.GetChildrenCount(panel), 1, 12);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(999));

        panel.SetVerticalOffset(double.PositiveInfinity);
        Layout(list, 600, 700);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(999));
        Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.InRange(VisualTreeHelper.GetChildrenCount(panel), 1, 12);

        Layout(list, 900, 700);
        Assert.InRange(VisualTreeHelper.GetChildrenCount(panel), 1, 18);
        items.RemoveAt(999);
        items.Move(998, 997);
        items[997] = "Replacement";
        Layout(list, 900, 700);
        Assert.InRange(VisualTreeHelper.GetChildrenCount(panel), 1, 18);

        // Changes outside the realized range must not corrupt generator indices.
        items.RemoveAt(0);
        items.Move(0, 1);
        Layout(list, 900, 700);
        list.ScrollIntoView(items[0]);
        Layout(list, 900, 700);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(items);
        view.Filter = value => ((string)value).EndsWith("5", StringComparison.Ordinal);
        Layout(list, 600, 700);
        Assert.InRange(VisualTreeHelper.GetChildrenCount(panel), 1, 12);
        view.Filter = null;

        items.Clear();
        items.Add("Only game");
        Layout(list, 600, 700);
        Assert.Equal(0, panel.VerticalOffset);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(panel));
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
    });

    private static void Layout(FrameworkElement control, double width, double height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
    }

    private static VirtualizingTilePanel? FindPanel(DependencyObject root)
    {
        if (root is VirtualizingTilePanel panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPanel(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF layout did not finish");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
