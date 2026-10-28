using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A row that lays its children out as a web page's wrapping flex row does (<c>display:flex; flex-wrap:wrap</c>): side by
/// side with a gap, starting a new line when the next one does not fit. On each line the children that may grow
/// (<see cref="GrowProperty"/>) share the space left, as text boxes do in the rule window. A child's starting width is
/// its own width, or the one <see cref="BasisProperty"/> gives; <see cref="FullLine"/> puts it on a line of its own.
/// </summary>
public sealed class FlexRow : Panel
{
    /// <summary>A starting width that takes the whole line (the HTML app's <c>flex-basis:100%</c>).</summary>
    public const double FullLine = double.PositiveInfinity;

    /// <summary>The space between two children on a line.</summary>
    public double Gap { get; set; } = 6;

    /// <summary>The space between two lines.</summary>
    public double LineGap { get; set; } = 6;

    /// <summary>How much of a line's spare width a child takes, against the others (CSS <c>flex-grow</c>); 0 for none.</summary>
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(double), typeof(FlexRow), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>A child's starting width (CSS <c>flex-basis</c>); NaN for its own width.</summary>
    public static readonly DependencyProperty BasisProperty = DependencyProperty.RegisterAttached(
        "Basis", typeof(double), typeof(FlexRow), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>How much a child grows.</summary>
    public static double GetGrow(UIElement element) => (double)element.GetValue(GrowProperty);

    /// <summary>Sets how much a child grows.</summary>
    public static void SetGrow(UIElement element, double value) => element.SetValue(GrowProperty, value);

    /// <summary>A child's starting width.</summary>
    public static double GetBasis(UIElement element) => (double)element.GetValue(BasisProperty);

    /// <summary>Sets a child's starting width.</summary>
    public static void SetBasis(UIElement element, double value) => element.SetValue(BasisProperty, value);

    /// <summary>Where each child goes, worked out by <see cref="MeasureOverride"/> and used by <see cref="ArrangeOverride"/>.</summary>
    private readonly List<Rect> places = [];

    /// <summary>The width the children were last laid out for in a measure.</summary>
    private double laidOutFor = double.NaN;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        foreach (UIElement child in InternalChildren) child.Measure(new Size(width, double.PositiveInfinity));
        Lay(width);
        // a child given less than its own width is measured again at the width it gets: arranged narrower than it
        // measured, WPF keeps its wider layout and cuts it off (a text box's button docked to its right lands past the edge)
        for (int i = 0; i < InternalChildren.Count; i++)
            if (places[i].Width + 0.5 < InternalChildren[i].DesiredSize.Width)
                InternalChildren[i].Measure(new Size(places[i].Width, double.PositiveInfinity));
        laidOutFor = width;
        double right = 0, bottom = 0;
        foreach (Rect place in places)
        {
            right = Math.Max(right, place.Right);
            bottom = Math.Max(bottom, place.Bottom);
        }
        return new Size(double.IsInfinity(width) ? right : Math.Min(right, width), bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // laid out again only for a new width: for the measured one, the children measured again to fit keep their places
        if (Math.Abs(finalSize.Width - laidOutFor) > 0.01) Lay(finalSize.Width);
        for (int i = 0; i < InternalChildren.Count; i++) InternalChildren[i].Arrange(places[i]);
        return finalSize;
    }

    /// <summary>Puts the children on lines within a width, and shares each line's spare width among those that grow.</summary>
    private void Lay(double width)
    {
        places.Clear();
        var line = new List<(UIElement Child, double Width)>();
        double top = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            double basis = GetBasis(child);
            double childWidth = double.IsNaN(basis) ? child.DesiredSize.Width : basis;
            // a child never starts wider than the line (CSS: it shrinks to fit); with no width to fill, at its own width
            childWidth = double.IsInfinity(width) ? Math.Min(childWidth, child.DesiredSize.Width) : Math.Min(childWidth, width);
            double used = line.Sum(x => x.Width) + Gap * line.Count;
            if (line.Count > 0 && used + childWidth > width + 0.01)
            {
                top = PlaceLine(line, width, top) + LineGap;
                line.Clear();
            }
            line.Add((child, childWidth));
        }
        if (line.Count > 0) PlaceLine(line, width, top);

        // collapsed children get no room; the list keeps one place per child, in order
        var placed = new Queue<Rect>(places);
        places.Clear();
        foreach (UIElement child in InternalChildren)
            places.Add(child.Visibility == Visibility.Collapsed ? new Rect() : placed.Dequeue());
    }

    /// <summary>Places one line of children at a height, each centred in the line's height; returns the line's bottom.</summary>
    private double PlaceLine(List<(UIElement Child, double Width)> line, double width, double top)
    {
        double used = line.Sum(x => x.Width) + Gap * (line.Count - 1);
        double spare = double.IsInfinity(width) ? 0 : Math.Max(0, width - used);
        double growth = line.Sum(x => GetGrow(x.Child));
        double height = line.Max(x => x.Child.DesiredSize.Height);
        double left = 0;
        foreach ((UIElement child, double childWidth) in line)
        {
            double extra = growth > 0 ? spare * GetGrow(child) / growth : 0;
            // a child grows no wider than its MaxWidth (CSS max-width); the rest of the line stays empty
            double finalWidth = Math.Min(childWidth + extra, child is FrameworkElement { MaxWidth: var most } ? most : double.PositiveInfinity);
            double childHeight = child.DesiredSize.Height;
            places.Add(new Rect(left, top + (height - childHeight) / 2, finalWidth, childHeight));
            left += finalWidth + Gap;
        }
        return top + height;
    }
}
