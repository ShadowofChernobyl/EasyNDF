using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// The icon on ▥ and ▤ of a <see cref="PanePair"/>: a window's outline, 14 pixels by 11, with the second pane filled in
/// on its right (beside) or along its bottom (below). It is drawn on whole pixels (see <see cref="PixelSnapping"/>), as
/// the symbols in the font came out blurred at this size, and in the button's text colour, so it follows the theme.
/// </summary>
public sealed class PaneIcon : FrameworkElement
{
    private const double IconWidth = 14, IconHeight = 11;

    /// <summary>The filled part's width beside, and height below, inside the outline.</summary>
    private const double PaneBeside = 5, PaneBelow = 4;

    /// <summary>The icon's colour: the text colour of the button it is on.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(PaneIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly PixelSnapping snapping;

    /// <summary>Makes an icon; <paramref name="beside"/> chooses which.</summary>
    public PaneIcon(bool beside)
    {
        IsBeside = beside;
        snapping = new PixelSnapping(this);
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Whether it shows the second pane beside; otherwise below.</summary>
    public bool IsBeside { get; }

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(IconWidth, IconHeight);

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        // the outline is four filled strips a pixel wide, so no edge falls between two pixels
        Fill(drawing, 0, 0, IconWidth, 1);
        Fill(drawing, 0, IconHeight - 1, IconWidth, 1);
        Fill(drawing, 0, 0, 1, IconHeight);
        Fill(drawing, IconWidth - 1, 0, 1, IconHeight);
        if (IsBeside) Fill(drawing, IconWidth - 1 - PaneBeside, 1, PaneBeside, IconHeight - 2);
        else Fill(drawing, 1, IconHeight - 1 - PaneBelow, IconWidth - 2, PaneBelow);
    }

    private void Fill(DrawingContext drawing, double left, double top, double width, double height) =>
        drawing.DrawRectangle(Foreground, null, snapping.Snap(left, top, width, height));
}
