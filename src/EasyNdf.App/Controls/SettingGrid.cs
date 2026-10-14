using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// The settings of one card on the Settings tab, in as many columns as fit (each at least <see cref="SmallestColumn"/>
/// wide), filled row by row as CSS's <c>grid-template-columns: repeat(auto-fill, minmax(...))</c> does. Each row is as
/// tall as the tallest setting in it, so the settings beside each other line up. A new setting is one more child.
/// </summary>
public sealed class SettingGrid : Panel
{
    /// <summary>The narrowest a column can be.</summary>
    private const double SmallestColumn = 340;

    /// <summary>The space between two columns.</summary>
    private const double ColumnGap = 18;

    /// <summary>How many columns fit in a width: one at the least.</summary>
    private static int ColumnsFor(double width) =>
        double.IsInfinity(width) ? 1 : Math.Max(1, (int)((width + ColumnGap) / (SmallestColumn + ColumnGap)));

    private static double ColumnWidth(double width, int columns) => (width - ColumnGap * (columns - 1)) / columns;

    private List<UIElement> Shown => [.. InternalChildren.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed)];

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnsFor(availableSize.Width);
        double width = double.IsInfinity(availableSize.Width) ? SmallestColumn : availableSize.Width;
        double columnWidth = ColumnWidth(width, columns);
        foreach (UIElement child in InternalChildren) child.Measure(new Size(columnWidth, double.PositiveInfinity));
        double height = RowHeights(Shown, columns).Sum();
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnsFor(finalSize.Width);
        double columnWidth = ColumnWidth(finalSize.Width, columns);
        List<UIElement> shown = Shown;
        List<double> rowHeights = RowHeights(shown, columns);
        double top = 0;
        for (int index = 0; index < shown.Count; index++)
        {
            int row = index / columns, column = index % columns;
            if (column == 0 && row > 0) top += rowHeights[row - 1];
            shown[index].Arrange(new Rect(column * (columnWidth + ColumnGap), top, columnWidth, rowHeights[row]));
        }
        return finalSize;
    }

    /// <summary>Each row's height: its tallest setting's.</summary>
    private static List<double> RowHeights(List<UIElement> shown, int columns)
    {
        var heights = new List<double>();
        for (int start = 0; start < shown.Count; start += columns)
            heights.Add(shown.Skip(start).Take(columns).Max(c => c.DesiredSize.Height));
        return heights;
    }
}
