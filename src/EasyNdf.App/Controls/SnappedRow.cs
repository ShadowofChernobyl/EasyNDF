using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A row of controls laid out the way a browser lays out a CSS flex row: each control is as wide as its content, to a
/// fraction of a pixel, with a gap between them, and the controls' left and right edges are then rounded to whole pixels.
/// Without that, after a few controls the row drifts a pixel away from the HTML app's.
/// </summary>
/// <remarks>
/// A control measured at its rounded width keeps that as its limit: when its words later grow, it is measured again
/// within it and the row is not told. So whoever changes a control's words calls <see cref="UIElement.InvalidateMeasure"/>
/// on the row, which measures every control afresh; a change of language does this by itself.
/// </remarks>
public sealed class SnappedRow : Panel
{
    /// <summary>The space between two controls, like CSS <c>gap</c>.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SnappedRow), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="GapProperty"/>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>
    /// Whether the controls go on a new line when the next one does not fit, as a CSS flex row with
    /// <c>flex-wrap:wrap</c> does; the lines are <see cref="Gap"/> apart. Off by default: the row stays on one line.
    /// </summary>
    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(
        nameof(Wrap), typeof(bool), typeof(SnappedRow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="WrapProperty"/>
    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }

    /// <summary>The row's controls report their exact widths, so it does not round them.</summary>
    public SnappedRow()
    {
        UseLayoutRounding = false;
        // another language gives the controls other words: the row is laid out again while it is shown
        Loaded += (_, _) =>
        {
            // Loaded can come twice without Unloaded between: the row listens once
            Text.Texts.LanguageChanged -= InvalidateMeasure;
            Text.Texts.LanguageChanged += InvalidateMeasure;
        };
        Unloaded += (_, _) => Text.Texts.LanguageChanged -= InvalidateMeasure;
    }

    /// <summary>
    /// Each control fills its whole-pixel slot exactly. One left at its own fractional width would draw its right edge
    /// outside its box, where it is cut off.
    /// </summary>
    protected override void OnVisualChildrenChanged(DependencyObject? added, DependencyObject? removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        if (added is FrameworkElement element) element.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Where each control goes: its left edge and width, in whole pixels, and its line, worked out while measuring.</summary>
    private readonly List<(double Left, double Width, int Line)> slots = [];

    /// <summary>The height of each line, worked out while measuring.</summary>
    private readonly List<double> lineHeights = [];

    /// <summary>
    /// Measures the controls as a browser lays out the row: first at their natural widths, to a fraction of a pixel; then
    /// again at the whole-pixel width their rounded edges leave them, which can be a fraction smaller. A browser shrinks the
    /// box to that width and lets its text overhang into the padding; measuring again lets the boxes here do the same.
    /// With <see cref="Wrap"/>, a control that would run past the width available starts a new line.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0;
        int line = 0;
        slots.Clear();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            // A hidden control takes no room, and no gap either.
            if (child.Visibility == Visibility.Collapsed)
            {
                slots.Add((Snap(x), 0, line));
                continue;
            }
            if (Wrap && x > 0 && x + child.DesiredSize.Width > availableSize.Width + 0.01)
            {
                line++;
                x = 0;
            }
            double left = Snap(x), right = Snap(x + child.DesiredSize.Width);
            slots.Add((left, right - left, line));
            x += child.DesiredSize.Width + Gap;
        }

        lineHeights.Clear();
        for (int i = 0; i <= line; i++) lineHeights.Add(0);
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            UIElement child = InternalChildren[i];
            child.Measure(new Size(slots[i].Width, availableSize.Height));
            lineHeights[slots[i].Line] = Math.Max(lineHeights[slots[i].Line], child.DesiredSize.Height);
        }
        double width = slots.Count == 0 ? 0 : slots.Max(slot => slot.Left + slot.Width);
        double height = lineHeights.Sum() + Gap * (lineHeights.Count - 1);
        return new Size(width, height); // the exact height, as a browser keeps it: the boxes in it put their own edges on whole pixels
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // one line fills the height it is given, as before; several lines each take their own height, a gap apart
        bool oneLine = lineHeights.Count <= 1;
        for (int i = 0; i < InternalChildren.Count && i < slots.Count; i++)
        {
            int line = slots[i].Line;
            double top = lineHeights.Take(line).Sum() + Gap * line;
            double height = oneLine ? finalSize.Height : lineHeights[line];
            InternalChildren[i].Arrange(new Rect(slots[i].Left, top, slots[i].Width, height));
        }
        return finalSize;
    }

    /// <summary>A browser rounds an edge to the nearest whole pixel, halves up.</summary>
    private static double Snap(double position) => Math.Round(position, MidpointRounding.AwayFromZero);
}
