using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using vKOROBKU.App.Controls;

namespace vKOROBKU.Tests;

public sealed class VirtualizingTilePanelTests
{
    [Fact]
    public void InspectorReflow_KeepsLastCardOfEightColumnRowSelectedAndVisible() => OnSta(() =>
    {
        var list = CreateGameList();
        list.ItemsSource = Enumerable.Range(0, 1000).Select(i => $"Game {i}").ToArray();
        var wide = VirtualizingTilePanel.TileWidth * 8 + 24;
        var narrow = VirtualizingTilePanel.TileWidth * 5 + 24;
        Layout(list, wide, 600);
        var panel = FindPanel(list)!;
        panel.SetVerticalOffset(9 * VirtualizingTilePanel.TileHeight);
        Layout(list, wide, 600);
        list.SelectedIndex = 87; // Last card of row 11, after scrolling.
        var selected = list.SelectedItem;
        var container = list.ItemContainerGenerator.ContainerFromIndex(87);
        Assert.NotNull(container);

        foreach (var width in new[] { narrow, wide, narrow })
        {
            Layout(list, width, 600);
            Assert.Same(selected, list.SelectedItem);
            Assert.Equal(87, list.SelectedIndex);
            Assert.Same(container, list.ItemContainerGenerator.ContainerFromIndex(87));
            var tile = (ListBoxItem)container;
            Assert.True(tile.IsSelected);
            var position = tile.TranslatePoint(new Point(), panel);
            Assert.InRange(position.Y, 0, 600 - VirtualizingTilePanel.TileHeight);
        }
    });

    [Fact]
    public void GameListRejectsDragSelectionCapture_ButCheckboxKeepsItsOwnCapture() => OnSta(() =>
    {
        var standard = new ListBox();
        var games = CreateGameList();
        bool? retainedCapture = null;
        games.IsMouseCapturedChanged += (_, e) =>
        {
            if (e.NewValue is true) retainedCapture = games.IsMouseCaptured;
        };
        var checkbox = new CheckBox { Content = "Queue" };
        games.Items.Add(new ListBoxItem { Content = checkbox });
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(standard);
        Grid.SetRow(games, 1);
        grid.Children.Add(games);
        var window = new Window { Content = grid, Width = 600, Height = 600,
            Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(standard.CaptureMouse()); // Verify this test has a working input source.
            standard.ReleaseMouseCapture();
            games.CaptureMouse();
            Assert.Equal(false, retainedCapture);
            Assert.False(games.IsMouseCaptured);
            Assert.True(checkbox.CaptureMouse());
            Assert.True(checkbox.IsMouseCaptured);
        }
        finally
        {
            Mouse.Capture(null);
            window.Close();
        }
    });

    private static GameLibraryListBox CreateGameList()
    {
        var list = new GameLibraryListBox
        {
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingTilePanel)))
        };
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        list.Template = new ControlTemplate(typeof(ListBox)) { VisualTree = scroll };
        return list;
    }

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
