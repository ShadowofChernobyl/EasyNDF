using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A row of controls that wraps onto more lines when it runs out of room, with the same gap across and down (the HTML
/// app's <c>.bar</c>: <c>display: flex; flex-wrap: wrap; gap: 6px</c>). Edges land on whole pixels, as in
/// <see cref="SnappedRow"/>.
/// </summary>
public sealed class WrapRow : Panel
{
    /// <summary>The gap between controls, across and down.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(WrapRow), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="GapProperty"/>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private readonly List<Rect> places = [];

    /// <summary>Lays the controls out line by line: a control that does not fit on the line starts the next one.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        places.Clear();
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) { places.Add(Rect.Empty); continue; }
            Size size = child.DesiredSize;
            if (x > 0 && x + size.Width > availableSize.Width)
            {
                x = 0;
                y += lineHeight + Gap;
                lineHeight = 0;
            }
            // the right edge on a whole pixel too, but never left of where the control ends: rounded down, the control
            // would get less than its own width, and WPF would cut off its right edge
            double left = Snap(x), right = Math.Max(Snap(x + size.Width), left + Math.Ceiling(size.Width));
            places.Add(new Rect(left, y, right - left, size.Height));
            x += size.Width + Gap;
            lineHeight = Math.Max(lineHeight, size.Height);
            widest = Math.Max(widest, right);
        }
        return new Size(widest, y + lineHeight);
    }

    /// <summary>Puts each control in its place; on a line, controls are centred top to bottom, as align-items: center does.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        // the height of each line, to centre the controls on it
        var lineHeights = new Dictionary<double, double>();
        foreach (Rect place in places.Where(p => !p.IsEmpty))
            lineHeights[place.Y] = Math.Max(lineHeights.GetValueOrDefault(place.Y), place.Height);
        for (int i = 0; i < InternalChildren.Count && i < places.Count; i++)
        {
            Rect place = places[i];
            if (place.IsEmpty) { InternalChildren[i].Arrange(new Rect()); continue; }
            double top = place.Y + (lineHeights[place.Y] - place.Height) / 2;
            InternalChildren[i].Arrange(new Rect(place.X, top, place.Width, place.Height));
        }
        return finalSize;
    }

    /// <summary>A browser rounds an edge to the nearest whole pixel, halves up.</summary>
    private static double Snap(double position) => Math.Round(position, MidpointRounding.AwayFromZero);
}
