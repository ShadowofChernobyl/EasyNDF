using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A line of a <see cref="LinesPanel"/> list whose height is the same as every other line of its kind: a row of cards,
/// and a group's last row, which keeps a gap under it, are two kinds. A line that is not one is of its type's kind.
/// </summary>
public interface ILineKind
{
    /// <summary>What sets the line apart from the other lines of its type, such as being a group's last row.</summary>
    object Kind { get; }
}

/// <summary>
/// The panel of a long list of lines (the Armory's rows of cards under their headings): only the lines in sight, and
/// half a screen above and below, are made, as in WPF's VirtualizingStackPanel. That panel guesses the height of the
/// lines it has not made from those it has, and corrects the guess as more are made, so the scrollbar's thumb jumped
/// back and forth while new rows came into sight. Here one line of each kind is measured, and the lines of a kind are
/// all as tall (<see cref="ILineKind"/>), so the place of every line is known and the thumb moves evenly.
/// </summary>
public sealed class LinesPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>How far the arrow keys and the scrollbar's arrows scroll, as in WPF's own lists.</summary>
    private const double LineStep = 16;

    /// <summary>How far a notch of the wheel scrolls where SmoothScrolling does not take it, as in WPF's own lists.</summary>
    private const double WheelStep = 48;

    /// <summary>The height of a line of each kind, measured on one of them.</summary>
    private readonly Dictionary<(Type, object?), double> kindHeights = [];

    /// <summary>The height of each line measured so far, by its place in the list, for a kind whose lines differ.</summary>
    private readonly Dictionary<int, double> madeHeights = [];

    /// <summary>Where each line starts; the last entry is where the last line ends, the height of the whole list.</summary>
    private double[] tops = [0];

    /// <summary>The width the heights were measured at; a line wider or narrower may be taller or shorter.</summary>
    private double measuredWidth = double.NaN;

    private Size viewport, extent;
    private double offset;

    protected override Size MeasureOverride(Size available)
    {
        // the generator is ready only once the children have been asked for
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;
        IList items = ItemsControl.GetItemsOwner(this)?.Items ?? (IList)Array.Empty<object>();
        double width = double.IsInfinity(available.Width) ? 0 : available.Width;
        if (width != measuredWidth)
        {
            kindHeights.Clear();
            madeHeights.Clear();
            measuredWidth = width;
        }

        // a line of each kind whose height is not known yet is made and measured
        foreach (int index in FirstOfEachUnknownKind(items))
        {
            UIElement line = Make(index, generator, children);
            line.Measure(new Size(width, double.PositiveInfinity));
            kindHeights[KindOf(items[index])] = line.DesiredSize.Height;
        }
        LayOut(items);

        double viewportHeight = double.IsInfinity(available.Height) ? tops[^1] : available.Height;
        offset = Math.Clamp(offset, 0, Math.Max(0, tops[^1] - viewportHeight));
        // the lines in sight, and half a screen above and below, so a short scroll shows lines already made
        int first = LineAt(offset - viewportHeight / 2, items.Count);
        int last = LineAt(offset + viewportHeight * 1.5, items.Count);
        LetGoOfLinesOutside(first, last, generator, children);
        bool heightsChanged = false;
        for (int index = first; index <= last && index < items.Count; index++)
        {
            UIElement line = Make(index, generator, children);
            line.Measure(new Size(width, double.PositiveInfinity));
            double height = line.DesiredSize.Height;
            if (madeHeights.TryGetValue(index, out double before) && before == height) continue;
            madeHeights[index] = height;
            if (height != kindHeights.GetValueOrDefault(KindOf(items[index]))) heightsChanged = true;
        }
        // a line of a kind whose lines differ in height moves the lines under it
        if (heightsChanged) LayOut(items);

        viewport = new Size(width, viewportHeight);
        extent = new Size(width, tops[^1]);
        offset = Math.Clamp(offset, 0, Math.Max(0, extent.Height - viewport.Height));
        ScrollOwner?.InvalidateScrollInfo();
        return new Size(width, Math.Min(viewportHeight, extent.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        UIElementCollection children = InternalChildren;
        for (int at = 0; at < children.Count; at++)
        {
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(at, 0));
            if (index < 0 || index + 1 >= tops.Length) continue;
            children[at].Arrange(new Rect(0, tops[index] - offset, finalSize.Width, tops[index + 1] - tops[index]));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
            case NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                // a new list: none of the lines made belong to it
                if (InternalChildren.Count > 0) RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }
        // the lines are new, or have moved: their heights are measured again, and their kinds' (a zoom changes them)
        kindHeights.Clear();
        madeHeights.Clear();
        base.OnItemsChanged(sender, args);
    }

    /// <summary>A line's kind: its type, and what sets it apart from others of its type (<see cref="ILineKind"/>).</summary>
    private static (Type, object?) KindOf(object item) => (item.GetType(), (item as ILineKind)?.Kind);

    /// <summary>The place of the first line of each kind whose height is not known yet.</summary>
    private List<int> FirstOfEachUnknownKind(IList items)
    {
        var found = new List<int>();
        var seen = new HashSet<(Type, object?)>();
        for (int index = 0; index < items.Count; index++)
        {
            (Type, object?) kind = KindOf(items[index]!);
            if (!kindHeights.ContainsKey(kind) && seen.Add(kind)) found.Add(index);
        }
        return found;
    }

    /// <summary>Where every line starts: from its own height once measured, else its kind's.</summary>
    private void LayOut(IList items)
    {
        if (tops.Length != items.Count + 1) tops = new double[items.Count + 1];
        double y = 0;
        for (int index = 0; index < items.Count; index++)
        {
            tops[index] = y;
            y += madeHeights.TryGetValue(index, out double made) ? made : kindHeights.GetValueOrDefault(KindOf(items[index]!));
        }
        tops[items.Count] = y;
    }

    /// <summary>The line at a height in the list: the first or the last when above or below it.</summary>
    private int LineAt(double y, int count)
    {
        if (count == 0) return 0;
        // the last line starting at or above y
        int found = Array.BinarySearch(tops, 0, count, y);
        if (found < 0) found = ~found - 1;
        return Math.Clamp(found, 0, count - 1);
    }

    /// <summary>The line's container, made now unless it is already; it is kept among the children in the list's order.</summary>
    private UIElement Make(int index, IItemContainerGenerator generator, UIElementCollection children)
    {
        GeneratorPosition position = generator.GeneratorPositionFromIndex(index);
        int childAt = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            var line = (UIElement)generator.GenerateNext(out _);
            // made already, or one let go of earlier and used again, which must be put back among the children
            if (childAt < children.Count && children[childAt] == line) return line;
            if (childAt >= children.Count) AddInternalChild(line);
            else InsertInternalChild(childAt, line);
            generator.PrepareItemContainer(line);
            return line;
        }
    }

    /// <summary>The lines out of reach are let go of, and kept to show other lines when the list scrolls.</summary>
    private void LetGoOfLinesOutside(int first, int last, IItemContainerGenerator generator, UIElementCollection children)
    {
        for (int at = children.Count - 1; at >= 0; at--)
        {
            var position = new GeneratorPosition(at, 0);
            int index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last) continue;
            ((IRecyclingItemContainerGenerator)generator).Recycle(position, 1);
            RemoveInternalChildRange(at, 1);
        }
    }

    /// <summary>Scrolls just enough for a line to be in sight, as for the unit chosen with the arrow keys.</summary>
    protected override void BringIndexIntoView(int index)
    {
        if (index < 0 || index + 1 >= tops.Length) return;
        if (tops[index] < offset) SetVerticalOffset(tops[index]);
        else if (tops[index + 1] > offset + viewport.Height) SetVerticalOffset(tops[index + 1] - viewport.Height);
    }

    // ---- scrolling ----

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => offset;
    public ScrollViewer? ScrollOwner { get; set; }

    public void SetVerticalOffset(double value)
    {
        value = Math.Clamp(value, 0, Math.Max(0, extent.Height - viewport.Height));
        if (value == offset) return;
        offset = value;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void LineUp() => SetVerticalOffset(offset - LineStep);
    public void LineDown() => SetVerticalOffset(offset + LineStep);
    public void PageUp() => SetVerticalOffset(offset - viewport.Height);
    public void PageDown() => SetVerticalOffset(offset + viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(offset - WheelStep);
    public void MouseWheelDown() => SetVerticalOffset(offset + WheelStep);

    // the lines are as wide as the list, so it never scrolls sideways
    public void SetHorizontalOffset(double value) { }
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    /// <summary>Scrolls just enough for part of a line to be in sight, as when a tile in it takes the focus.</summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        UIElementCollection children = InternalChildren;
        for (int at = 0; at < children.Count; at++)
        {
            UIElement line = children[at];
            if (line != visual && !line.IsAncestorOf(visual)) continue;
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(at, 0));
            if (index < 0 || index + 1 >= tops.Length) break;
            Rect inLine = visual.TransformToAncestor(line).TransformBounds(rectangle);
            double top = tops[index] + inLine.Top, bottom = tops[index] + inLine.Bottom;
            if (top < offset) SetVerticalOffset(top);
            else if (bottom > offset + viewport.Height) SetVerticalOffset(Math.Min(top, bottom - viewport.Height));
            break;
        }
        return rectangle;
    }
}
