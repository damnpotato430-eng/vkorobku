using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace vKOROBKU.App.Controls;

/// <summary>Fixed-size game tiles. Only viewport rows are realized, with one row
/// of overscan on either side. Off-screen images and bindings can be collected.</summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    public const double TileWidth = 286;
    public const double TileHeight = 284;
    private int _columns = 1;
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth { get; private set; }
    public double ExtentHeight { get; private set; }
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }
    public double HorizontalOffset => 0;
    public double VerticalOffset { get; private set; }
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var count = owner?.Items.Count ?? 0;
        var previousColumns = _columns;
        var firstVisibleIndex = (int)(VerticalOffset / TileHeight) * previousColumns;
        var selectedIndex = (owner as Selector)?.SelectedIndex ?? -1;
        var previousSelectedTop = selectedIndex / previousColumns * TileHeight;
        var selectionWasVisible = selectedIndex >= 0 &&
            previousSelectedTop + TileHeight > VerticalOffset && previousSelectedTop < VerticalOffset + ViewportHeight;
        ViewportWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : TileWidth;
        ViewportHeight = double.IsFinite(availableSize.Height) ? availableSize.Height : TileHeight;
        _columns = Math.Max(1, (int)(ViewportWidth / TileWidth));
        ExtentWidth = ViewportWidth;
        ExtentHeight = Math.Ceiling(count / (double)_columns) * TileHeight;
        if (_columns != previousColumns)
        {
            // Anchor the visible content when the inspector changes the column count.
            // Keep the clicked/focused tile alive before recycling off-screen rows.
            VerticalOffset = firstVisibleIndex / _columns * TileHeight + VerticalOffset % TileHeight;
            if (selectionWasVisible)
            {
                var selectedTop = selectedIndex / _columns * TileHeight;
                VerticalOffset = Math.Min(VerticalOffset, selectedTop);
                VerticalOffset = Math.Max(VerticalOffset, selectedTop + TileHeight - ViewportHeight);
            }
        }
        VerticalOffset = Math.Clamp(VerticalOffset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        ScrollOwner?.InvalidateScrollInfo();

        var first = Math.Max(0, (int)(VerticalOffset / TileHeight) - 1) * _columns;
        var last = Math.Min(count - 1,
            ((int)Math.Ceiling((VerticalOffset + ViewportHeight) / TileHeight) + 1) * _columns - 1);
        // Accessing InternalChildren initializes the panel's generator.
        _ = InternalChildren;
        var generator = ItemContainerGenerator;
        for (var child = InternalChildren.Count - 1; child >= 0; child--)
        {
            var position = new GeneratorPosition(child, 0);
            var index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last)
                continue;
            generator.Remove(position, 1);
            RemoveInternalChildRange(child, 1);
        }
        if (count > 0 && last >= first)
        {
            var position = generator.GeneratorPositionFromIndex(first);
            var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
            using (generator.StartAt(position, GeneratorDirection.Forward, true))
            {
                for (var index = first; index <= last; index++, childIndex++)
                {
                    var child = (UIElement)generator.GenerateNext(out var isNew);
                    if (isNew)
                    {
                        if (childIndex == InternalChildren.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }
                    child.Measure(new Size(TileWidth, TileHeight));
                }
            }
        }
        return new Size(ViewportWidth, ViewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var child = 0; child < InternalChildren.Count; child++)
        {
            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(child, 0));
            InternalChildren[child].Arrange(new Rect(index % _columns * TileWidth,
                index / _columns * TileHeight - VerticalOffset, TileWidth, TileHeight));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        else if (args.ItemUICount > 0 && args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
            RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
        else if (args.ItemUICount > 0 && args.Action == NotifyCollectionChangedAction.Move)
            RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        var count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
        if (index < 0 || index >= count)
            return;
        var top = index / _columns * TileHeight;
        if (top < VerticalOffset)
            SetVerticalOffset(top);
        else if (top + TileHeight > VerticalOffset + ViewportHeight)
            SetVerticalOffset(top + TileHeight - ViewportHeight);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null)
            return Rect.Empty;
        var container = ItemsControl.ContainerFromElement(owner, visual);
        if (container is null)
            return Rect.Empty;
        var index = owner.ItemContainerGenerator.IndexFromContainer(container);
        BringIndexIntoView(index);
        return new Rect(index % _columns * TileWidth,
            index / _columns * TileHeight - VerticalOffset, TileWidth, TileHeight);
    }

    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        var next = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (next == VerticalOffset) return;
        VerticalOffset = next;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }
    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(VerticalOffset - 32);
    public void LineDown() => SetVerticalOffset(VerticalOffset + 32);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 96);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 96);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
