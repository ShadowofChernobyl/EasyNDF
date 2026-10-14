using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A one-pixel edge whose top and left sides have one colour and whose bottom and right sides have another: the HTML app's
/// <c>.raised</c> (light top-left, dark bottom-right) and <c>.sunken</c> (the other way round) look. WPF's own Border can only
/// give all four sides one colour.
/// </summary>
public sealed class Bevel : Decorator
{
    /// <summary>The colour of the top and left edges.</summary>
    public static readonly DependencyProperty TopLeftProperty = DependencyProperty.Register(
        nameof(TopLeft), typeof(Brush), typeof(Bevel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The colour of the bottom and right edges.</summary>
    public static readonly DependencyProperty BottomRightProperty = DependencyProperty.Register(
        nameof(BottomRight), typeof(Brush), typeof(Bevel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The colour inside the edges.</summary>
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(Bevel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Space between the edges and the content.</summary>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(Bevel), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Which sides have an edge. The tabs have none at the bottom, for example.</summary>
    public static readonly DependencyProperty EdgesProperty = DependencyProperty.Register(
        nameof(Edges), typeof(Thickness), typeof(Bevel), new FrameworkPropertyMetadata(new Thickness(1), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="TopLeftProperty"/>
    public Brush? TopLeft { get => (Brush?)GetValue(TopLeftProperty); set => SetValue(TopLeftProperty, value); }

    /// <inheritdoc cref="BottomRightProperty"/>
    public Brush? BottomRight { get => (Brush?)GetValue(BottomRightProperty); set => SetValue(BottomRightProperty, value); }

    /// <inheritdoc cref="BackgroundProperty"/>
    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    /// <inheritdoc cref="PaddingProperty"/>
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <inheritdoc cref="EdgesProperty"/>
    public Thickness Edges { get => (Thickness)GetValue(EdgesProperty); set => SetValue(EdgesProperty, value); }

    /// <summary>The edges and padding together: the space around the content.</summary>
    private Thickness Around => new(
        Edges.Left + Padding.Left, Edges.Top + Padding.Top, Edges.Right + Padding.Right, Edges.Bottom + Padding.Bottom);

    protected override Size MeasureOverride(Size constraint)
    {
        Thickness around = Around;
        double width = around.Left + around.Right, height = around.Top + around.Bottom;
        if (Child is null) return new Size(width, height);
        Child.Measure(new Size(Math.Max(0, constraint.Width - width), Math.Max(0, constraint.Height - height)));
        // Never wider than offered: a row of whole-pixel slots (SnappedRow) can offer a fraction less than the content
        // wants, and then, as in a browser, the box takes the slot and its content overhangs into the padding.
        return new Size(Math.Min(Child.DesiredSize.Width + width, constraint.Width), Child.DesiredSize.Height + height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness around = Around;
        // The content gets at least its own width, so a box a fraction narrower than it never cuts it off.
        double contentWidth = Math.Max(arrangeSize.Width - around.Left - around.Right, Child?.DesiredSize.Width ?? 0);
        Child?.Arrange(new Rect(around.Left, around.Top, Math.Max(0, contentWidth), Math.Max(0, arrangeSize.Height - around.Top - around.Bottom)));
        return arrangeSize;
    }

    private readonly PixelSnapping snapping;

    /// <summary>Builds the edge.</summary>
    public Bevel() => snapping = new PixelSnapping(this);

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        Thickness edges = Edges;
        if (Background is not null) drawing.DrawRectangle(Background, null, snapping.Snap(0, 0, width, height));
        if (TopLeft is not null)
        {
            drawing.DrawRectangle(TopLeft, null, snapping.Snap(0, 0, width, edges.Top));
            drawing.DrawRectangle(TopLeft, null, snapping.Snap(0, 0, edges.Left, height));
        }
        if (BottomRight is not null)
        {
            drawing.DrawRectangle(BottomRight, null, snapping.Snap(0, height - edges.Bottom, width, edges.Bottom));
            drawing.DrawRectangle(BottomRight, null, snapping.Snap(width - edges.Right, 0, edges.Right, height));
        }
    }
}
