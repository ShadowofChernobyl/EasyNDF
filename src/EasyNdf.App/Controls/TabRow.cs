using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A row of tabs, side by side, which share the width when they do not all fit, as a browser's tabs do: the widest are
/// made narrower first, each down to its least width (its name then ends in "…"); past that the row runs on out of sight.
/// </summary>
public sealed class TabRow : Panel
{
    /// <summary>The width each tab was given at the last measure.</summary>
    private double[] widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = InternalChildren.Count;
        var wants = new double[count];
        var least = new double[count];
        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            wants[i] = child.DesiredSize.Width;
            least[i] = child is FrameworkElement tab ? Math.Min(wants[i], tab.MinWidth + tab.Margin.Left + tab.Margin.Right) : wants[i];
        }
        widths = Shared(wants, least, availableSize.Width);
        double height = 0;
        for (int i = 0; i < count; i++)
        {
            // a tab with all it wants keeps no width of its own: WPF measures a tab again by itself (its name changed, or
            // turned bold) with the width it was last given, so one given less once would stay cut short
            bool narrowed = widths[i] < wants[i];
            InternalChildren[i].Measure(new Size(narrowed ? widths[i] : double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }
        double total = widths.Sum();
        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double left = 0;
        for (int i = 0; i < InternalChildren.Count && i < widths.Length; i++)
        {
            InternalChildren[i].Arrange(new Rect(left, 0, widths[i], finalSize.Height));
            left += widths[i];
        }
        return finalSize;
    }

    /// <summary>
    /// The widths that fit the room: every tab wider than a cap is brought down to it, never below its least width. The
    /// cap is the widest that fits, found by halving.
    /// </summary>
    private static double[] Shared(double[] wants, double[] least, double room)
    {
        if (double.IsInfinity(room) || wants.Sum() <= room) return wants;
        double Total(double cap) => wants.Select((want, i) => Math.Max(Math.Min(want, cap), least[i])).Sum();
        double low = 0, high = wants.Max();
        for (int step = 0; step < 30; step++)
        {
            double cap = (low + high) / 2;
            if (Total(cap) > room) high = cap;
            else low = cap;
        }
        return [.. wants.Select((want, i) => Math.Max(Math.Min(want, low), least[i]))];
    }
}
