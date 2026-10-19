using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// The settings of one card on the Settings tab, one under the other, like a list of properties: each name in a
/// column at the left with its control just beside it, so the eye runs straight down, and the rows shaded in turn
/// across the whole card. A new setting is one more child.
/// </summary>
public sealed class SettingGrid : Panel
{
    /// <summary>The names' column: 250 pixels, or 40% of a narrow list, so its controls keep room beside it.</summary>
    private const double NameWidth = 250, NameShareWhenNarrow = 0.4;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width;
        double height = 0;
        int shownIndex = 0;
        var nameWidth = new GridLength(double.IsInfinity(width) ? NameWidth : Math.Min(NameWidth, Math.Floor(width * NameShareWhenNarrow)));
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            // every other row shaded, counting only those shown, so a search's results stay shaded in turn too
            if (child is SettingRow row)
            {
                row.IsShaded = shownIndex % 2 == 1;
                row.NameWidth = nameWidth;
            }
            shownIndex++;
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(width) ? 0 : width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = finalSize.Width;
        double top = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Arrange(new Rect(0, top, width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
