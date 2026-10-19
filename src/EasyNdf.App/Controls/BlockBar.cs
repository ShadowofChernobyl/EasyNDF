using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// A loading bar that fills block by block, like Steam's: flat blocks in the theme's accent colour, 2 pixels apart, in a
/// sunken track that stays empty ahead of them. It is always the same size, whatever the window's, and is drawn on whole
/// pixels (see <see cref="PixelSnapping"/>).
/// </summary>
public sealed class BlockBar : FrameworkElement
{
    /// <summary>How many blocks fill the bar.</summary>
    public const int Blocks = 30;

    private const double BlockWidth = 10, BlockHeight = 12, BlockGap = 2;

    /// <summary>The track's edge (1 pixel) and the space inside it around the blocks (2 pixels).</summary>
    private const double Edge = 1, Inset = 2;

    /// <summary>How full the bar is, from 0 to 1; it shows whole blocks only.</summary>
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(BlockBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="FractionProperty"/>
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    private readonly PixelSnapping snapping;

    /// <summary>Makes an empty bar.</summary>
    public BlockBar()
    {
        snapping = new PixelSnapping(this);
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The number of blocks shown: the whole blocks the fraction fills.</summary>
    public int FilledBlocks => (int)Math.Floor(Math.Clamp(Fraction, 0, 1) * Blocks);

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) =>
        new(2 * (Edge + Inset) + Blocks * BlockWidth + (Blocks - 1) * BlockGap, 2 * (Edge + Inset) + BlockHeight);

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawing)
    {
        Size size = DesiredSize;
        double width = size.Width - Margin.Left - Margin.Right, height = size.Height - Margin.Top - Margin.Bottom;
        // the sunken track: dark on its top and left, light on its bottom and right, as the app's sunken boxes
        Fill(drawing, "well", 0, 0, width, height);
        Fill(drawing, "lo", 0, 0, width, Edge);
        Fill(drawing, "lo", 0, 0, Edge, height);
        Fill(drawing, "hi", 0, height - Edge, width, Edge);
        Fill(drawing, "hi", width - Edge, 0, Edge, height);
        for (int block = 0; block < FilledBlocks; block++)
            Fill(drawing, "accent", Edge + Inset + block * (BlockWidth + BlockGap), Edge + Inset, BlockWidth, BlockHeight);
    }

    private void Fill(DrawingContext drawing, string colour, double left, double top, double width, double height)
    {
        if (TryFindResource(colour) is Brush brush) drawing.DrawRectangle(brush, null, snapping.Snap(left, top, width, height));
    }
}
