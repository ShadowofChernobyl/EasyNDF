using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>The views the Units tab's icon buttons stand for.</summary>
public enum ViewShape
{
    /// <summary>Cards: four tiles, two by two.</summary>
    Cards,
    /// <summary>Table: a heading, then rows, between two sides.</summary>
    Table,
}

/// <summary>
/// The icon on the Units tab's Cards and Table buttons, 14 pixels by 11 like the pane buttons' (<see cref="PaneIcon"/>):
/// drawn on whole pixels (see <see cref="PixelSnapping"/>), in the button's text colour, so it follows the theme and
/// turns green with the button when it is pressed.
/// </summary>
public sealed class ViewIcon : FrameworkElement
{
    private const double IconWidth = 14, IconHeight = 11;

    /// <summary>The icon's colour: the text colour of the button it is on.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(ViewIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly PixelSnapping snapping;

    /// <summary>Makes an icon; <see cref="Shape"/> chooses which.</summary>
    public ViewIcon()
    {
        snapping = new PixelSnapping(this);
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Which view the icon stands for.</summary>
    public ViewShape Shape { get; set; }

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(IconWidth, IconHeight);

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        if (Shape == ViewShape.Cards) DrawCards(drawing);
        else DrawTable(drawing);
    }

    /// <summary>Four tiles, 6 pixels by 5, with 2 pixels between them across and 1 down.</summary>
    private void DrawCards(DrawingContext drawing)
    {
        Fill(drawing, 0, 0, 6, 5);
        Fill(drawing, 8, 0, 6, 5);
        Fill(drawing, 0, 6, 6, 5);
        Fill(drawing, 8, 6, 6, 5);
    }

    /// <summary>A heading 2 pixels tall, four rows of 1 pixel under it, and the two sides joining them.</summary>
    private void DrawTable(DrawingContext drawing)
    {
        Fill(drawing, 0, 0, IconWidth, 2);
        for (double row = 3; row < IconHeight; row += 2) Fill(drawing, 0, row, IconWidth, 1);
        Fill(drawing, 0, 0, 1, IconHeight);
        Fill(drawing, IconWidth - 1, 0, 1, IconHeight);
    }

    private void Fill(DrawingContext drawing, double left, double top, double width, double height) =>
        drawing.DrawRectangle(Foreground, null, snapping.Snap(left, top, width, height));
}
