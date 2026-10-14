using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A dropdown's arrow: a triangle 10 pixels wide and 5 tall, pointing down. A browser draws it on whole pixels wherever
/// the dropdown sits, so it stays sharp; so does this (see <see cref="PixelSnapping"/>).
/// </summary>
public sealed class DropArrow : FrameworkElement
{
    private const double ArrowWidth = 10, ArrowHeight = 5;

    /// <summary>The arrow's colour.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DropArrow),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly PixelSnapping snapping;

    /// <summary>Makes an arrow.</summary>
    public DropArrow() => snapping = new PixelSnapping(this);

    /// <inheritdoc cref="FillProperty"/>
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(ArrowWidth, ArrowHeight);

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        Rect box = snapping.Snap(0, 0, ArrowWidth, ArrowHeight);
        var triangle = new StreamGeometry();
        using (StreamGeometryContext points = triangle.Open())
        {
            points.BeginFigure(box.TopLeft, isFilled: true, isClosed: true);
            points.LineTo(box.TopRight, isStroked: false, isSmoothJoin: false);
            points.LineTo(new Point(box.Left + ArrowWidth / 2, box.Bottom), isStroked: false, isSmoothJoin: false);
        }
        triangle.Freeze();
        drawing.DrawGeometry(Fill, null, triangle);
    }
}
