using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A row of controls laid out the way a browser lays out a CSS flex row: each control is as wide as its content, to a
/// fraction of a pixel, with a gap between them, and the controls' left and right edges are then rounded to whole pixels.
/// Without that, after a few controls the row drifts a pixel away from the HTML app's.
/// </summary>
public sealed class SnappedRow : Panel
{
    /// <summary>The space between two controls, like CSS <c>gap</c>.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SnappedRow), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="GapProperty"/>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>The row's controls report their exact widths, so it does not round them.</summary>
    public SnappedRow() => UseLayoutRounding = false;

    /// <summary>
    /// Each control fills its whole-pixel slot exactly. One left at its own fractional width would draw its right edge
    /// outside its box, where it is cut off.
    /// </summary>
    protected override void OnVisualChildrenChanged(DependencyObject? added, DependencyObject? removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        if (added is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Where each control goes: its left edge and width, in whole pixels, worked out while measuring.</summary>
    private readonly List<(double Left, double Width)> slots = [];

    /// <summary>
    /// Measures the controls as a browser lays out the row: first at their natural widths, to a fraction of a pixel; then
    /// again at the whole-pixel width their rounded edges leave them, which can be a fraction smaller. A browser shrinks the
    /// box to that width and lets its text overhang into the padding; measuring again lets the boxes here do the same.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, height = 0;
        slots.Clear();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            // A hidden control takes no room, and no gap either.
            if (child.Visibility == Visibility.Collapsed)
            {
                slots.Add((Snap(x), 0));
                continue;
            }
            double left = Snap(x), right = Snap(x + child.DesiredSize.Width);
            slots.Add((left, right - left));
            x += child.DesiredSize.Width + Gap;
        }
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            child.Measure(new Size(slots[i].Width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        double width = slots.Count == 0 ? 0 : slots.Max(slot => slot.Left + slot.Width);
        return new Size(width, height); // the exact height, as a browser keeps it: the boxes in it put their own edges on whole pixels
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count && i < slots.Count; i++)
            InternalChildren[i].Arrange(new Rect(slots[i].Left, 0, slots[i].Width, finalSize.Height));
        return finalSize;
    }

    /// <summary>A browser rounds an edge to the nearest whole pixel, halves up.</summary>
    private static double Snap(double position) => Math.Round(position, MidpointRounding.AwayFromZero);
}
