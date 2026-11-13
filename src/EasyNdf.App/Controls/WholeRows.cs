using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// Around a list's scrolling box: a list too long for its room stops after the last row that fits whole at the top of
/// the list, and what is left of the room stays empty, so the list does not end on a row cut in half. The list can be
/// a scrolling box, or a list that scrolls by itself and makes only the rows in sight.
/// </summary>
public sealed class WholeRows : Decorator
{
    /// <summary>The height last worked out for a list that makes only the rows in sight, kept while it is scrolled.</summary>
    private double whole = double.PositiveInfinity;

    /// <summary>Makes the box, which follows the list as rows come and go.</summary>
    public WholeRows() => Loaded += (_, _) =>
    {
        if (ScrollerOf(Child) is ScrollViewer scroller)
        {
            scroller.ScrollChanged -= ScrollerChanged;
            scroller.ScrollChanged += ScrollerChanged;
        }
    };

    private static ScrollViewer? ScrollerOf(UIElement? child) => child switch
    {
        ScrollViewer scroller => scroller,
        ItemsControl list when list.ApplyTemplate() || list.Template is not null => list.Template?.FindName("Scroller", list) as ScrollViewer,
        _ => null,
    };

    private void ScrollerChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0) InvalidateArrange();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        // a list that makes only the rows in sight scrolls by the height it is measured at, or its last row would stay out of sight
        Child?.Measure(Child is ItemsControl ? new Size(constraint.Width, Math.Min(constraint.Height, whole)) : constraint);
        return new Size(Child?.DesiredSize.Width ?? 0, Child is ItemsControl ? constraint.Height : Child?.DesiredSize.Height ?? 0);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null) return arrangeSize;
        if (Child is ItemsControl list)
        {
            double shown = Math.Min(arrangeSize.Height, whole);
            Child.Arrange(new Rect(0, 0, arrangeSize.Width, shown));
            whole = WholeHeightOf(list, arrangeSize.Height);
            // measured again at the height found; the rows just past it stay made, so it comes to the same height again
            if (Math.Abs(Math.Min(arrangeSize.Height, whole) - shown) > 0.5) InvalidateMeasure();
            return arrangeSize;
        }
        // the rows have their places only once the list is laid out at its full room
        Child.Arrange(new Rect(arrangeSize));
        double height = WholeHeight(arrangeSize.Height);
        if (height < arrangeSize.Height) Child.Arrange(new Rect(0, 0, arrangeSize.Width, height));
        return arrangeSize;
    }

    /// <summary>
    /// The same for a list that makes only the rows in sight: worked out with the list at its top, and kept while it is
    /// scrolled, as the height of a list must not change under the mouse wheel.
    /// </summary>
    private double WholeHeightOf(ItemsControl list, double room)
    {
        if (ScrollerOf(list) is not ScrollViewer scroller || scroller.VerticalOffset > 0) return whole;
        if (ItemsHostOf(list) is not Panel rows) return room;
        double fits = 0;
        foreach (UIElement row in rows.Children)
        {
            double bottom = row.TranslatePoint(new Point(0, row.RenderSize.Height), list).Y;
            if (bottom > room) return fits > 0 ? fits : room;
            fits = bottom;
        }
        return room;
    }

    /// <summary>The height that ends on a whole row: the room itself when the rows all fit, or none does.</summary>
    private double WholeHeight(double room)
    {
        if (Child is not ScrollViewer { Content: UIElement content }) return room;
        double fits = 0;
        foreach (UIElement row in RowsIn(content))
        {
            double bottom = row.TranslatePoint(new Point(0, row.RenderSize.Height), content).Y;
            if (bottom > room) return fits > 0 ? fits : room;
            fits = bottom;
        }
        return room;
    }

    /// <summary>The rows of a list, top to bottom: those of a vertical stack and of an items list, however they are nested.</summary>
    private static IEnumerable<UIElement> RowsIn(UIElement element)
    {
        if (element.Visibility != Visibility.Visible) yield break;
        Panel? stack = element switch
        {
            StackPanel { Orientation: Orientation.Vertical } panel => panel,
            ItemsControl items => ItemsHostOf(items),
            _ => null,
        };
        if (stack is null) { yield return element; yield break; }
        foreach (UIElement child in stack.Children)
            foreach (UIElement row in RowsIn(child))
                yield return row;
    }

    private static Panel? ItemsHostOf(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Panel { IsItemsHost: true } host) return host;
            if (ItemsHostOf(child) is Panel found) return found;
        }
        return null;
    }
}
