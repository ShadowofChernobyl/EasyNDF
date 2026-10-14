using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>
/// A column that stacks its children with a gap and at least fills the height it is given (its <c>MinHeight</c> inside a
/// scrolling box), as the rule window's <c>#editor</c> does on a web page. Children marked with
/// <see cref="GrowBasisProperty"/> (the lists of conditions and actions, CSS <c>flex:1 1 7.5rem</c>) are that tall at
/// first and share whatever height is left over; the rest are as tall as they need.
/// </summary>
public sealed class FlexColumn : Panel
{
    /// <summary>The space between two children.</summary>
    public double Gap { get; set; } = 6;

    /// <summary>A growing child's starting height (CSS <c>flex-basis</c>), which is also its least; NaN for a child that does not grow.</summary>
    public static readonly DependencyProperty GrowBasisProperty = DependencyProperty.RegisterAttached(
        "GrowBasis", typeof(double), typeof(FlexColumn), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>A child's starting height, or NaN.</summary>
    public static double GetGrowBasis(UIElement element) => (double)element.GetValue(GrowBasisProperty);

    /// <summary>Sets a child's starting height; it then shares the spare height too.</summary>
    public static void SetGrowBasis(UIElement element, double value) => element.SetValue(GrowBasisProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        // the children that do not grow first: what is left of the least height is shared by the growing ones
        double width = 0, fixedHeight = 0, basisTotal = 0;
        List<UIElement> growing = [];
        int shown = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            shown++;
            double basis = GetGrowBasis(child);
            if (!double.IsNaN(basis)) { growing.Add(child); basisTotal += basis; continue; }
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            fixedHeight += child.DesiredSize.Height;
        }
        double gaps = Gap * Math.Max(0, shown - 1);
        spare = growing.Count == 0 ? 0 : Math.Max(0, MinHeight - fixedHeight - basisTotal - gaps);
        // a growing child is measured at the height it will get, so an editor in it lays out lines all the way down
        foreach (UIElement child in growing)
        {
            child.Measure(new Size(availableSize.Width, GetGrowBasis(child) + spare / growing.Count));
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new Size(width, Math.Max(fixedHeight + basisTotal + gaps, MinHeight));
    }

    /// <summary>The height the growing children share beyond their starting heights, worked out when measuring.</summary>
    private double spare;

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<UIElement> shown = [.. InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed)];
        int growing = shown.Count(c => !double.IsNaN(GetGrowBasis(c)));
        double top = 0;
        foreach (UIElement child in shown)
        {
            double basis = GetGrowBasis(child);
            double height = double.IsNaN(basis) ? child.DesiredSize.Height : basis + spare / growing;
            child.Arrange(new Rect(0, top, finalSize.Width, height));
            top += height + Gap;
        }
        return finalSize;
    }
}
