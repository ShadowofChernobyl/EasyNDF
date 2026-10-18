using System.Windows;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// A bar of whole blocks, as an old Steam progress bar fills: a sunken track with a block for each part of the value,
/// in the theme's bar colour, or in the "best" colour for the best of the units compared. Compare can also mark where
/// the pinned unit's value would reach, with a thin upright line.
/// </summary>
public sealed class BlockMeter : FrameworkElement
{
    /// <summary>Each block is 5 pixels wide, with 2 pixels between blocks.</summary>
    private const double BlockWidth = 5, BlockStep = 7;

    /// <summary>The value shown; null for none (an empty track).</summary>
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double?), null);

    /// <summary>The value a full bar stands for.</summary>
    public static readonly DependencyProperty FullBarProperty = Register(nameof(FullBar), typeof(double), 1.0);

    /// <summary>How many blocks the bar has: 14 on Compare, 6 in the table.</summary>
    public static readonly DependencyProperty BlocksProperty = Register(nameof(Blocks), typeof(int), 10, affectsMeasure: true);

    /// <summary>Whether the value is the best of the units compared, drawn in the "ok" colour.</summary>
    public static readonly DependencyProperty IsBestProperty = Register(nameof(IsBest), typeof(bool), false);

    /// <summary>The pinned unit's value, marked with a line; null for no mark.</summary>
    public static readonly DependencyProperty MarkerProperty = Register(nameof(Marker), typeof(double?), null);

    /// <summary>Whether the bar is the table's thinner one: 9 pixels high with 1 pixel above and below the blocks.</summary>
    public static readonly DependencyProperty IsThinProperty = Register(nameof(IsThin), typeof(bool), false, affectsMeasure: true);

    // the theme's colours, kept as properties so the bar redraws when the theme changes
    private static readonly DependencyProperty WellProperty = Register("Well", typeof(Brush), null);
    private static readonly DependencyProperty LoProperty = Register("Lo", typeof(Brush), null);
    private static readonly DependencyProperty HiProperty = Register("Hi", typeof(Brush), null);
    private static readonly DependencyProperty FillProperty = Register("Fill", typeof(Brush), null);
    private static readonly DependencyProperty BestFillProperty = Register("BestFill", typeof(Brush), null);
    private static readonly DependencyProperty MarkerBrushProperty = Register("MarkerBrush", typeof(Brush), null);

    private readonly PixelSnapping snapping;

    /// <summary>Builds an empty bar in the theme's colours.</summary>
    public BlockMeter()
    {
        snapping = new PixelSnapping(this);
        SetResourceReference(WellProperty, "well");
        SetResourceReference(LoProperty, "lo");
        SetResourceReference(HiProperty, "hi");
        SetResourceReference(FillProperty, "meter");
        SetResourceReference(BestFillProperty, "ok");
        SetResourceReference(MarkerBrushProperty, "ink");
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <inheritdoc cref="ValueProperty"/>
    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <inheritdoc cref="FullBarProperty"/>
    public double FullBar { get => (double)GetValue(FullBarProperty); set => SetValue(FullBarProperty, value); }

    /// <inheritdoc cref="BlocksProperty"/>
    public int Blocks { get => (int)GetValue(BlocksProperty); set => SetValue(BlocksProperty, value); }

    /// <inheritdoc cref="IsBestProperty"/>
    public bool IsBest { get => (bool)GetValue(IsBestProperty); set => SetValue(IsBestProperty, value); }

    /// <inheritdoc cref="MarkerProperty"/>
    public double? Marker { get => (double?)GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }

    /// <inheritdoc cref="IsThinProperty"/>
    public bool IsThin { get => (bool)GetValue(IsThinProperty); set => SetValue(IsThinProperty, value); }

    /// <summary>The space between the edge and the blocks: 2 pixels all round, or 1 above and below on a thin bar.</summary>
    private Thickness Inside => IsThin ? new Thickness(2, 1, 2, 1) : new Thickness(2);

    /// <summary>As wide as its blocks (7 pixels each, less the gap after the last) with its edges and padding: 12 pixels high, 9 when thin.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(Blocks * BlockStep + 5, IsThin ? 9 : 12);

    protected override void OnRender(DrawingContext drawing)
    {
        double width = ActualWidth, height = ActualHeight;
        var well = (Brush?)GetValue(WellProperty);
        var lo = (Brush?)GetValue(LoProperty);
        var hi = (Brush?)GetValue(HiProperty);
        // the sunken track: dark top and left edges, light bottom and right
        drawing.DrawRectangle(well, null, snapping.Snap(0, 0, width, height));
        drawing.DrawRectangle(lo, null, snapping.Snap(0, 0, width, 1));
        drawing.DrawRectangle(lo, null, snapping.Snap(0, 0, 1, height));
        drawing.DrawRectangle(hi, null, snapping.Snap(0, height - 1, width, 1));
        drawing.DrawRectangle(hi, null, snapping.Snap(width - 1, 0, 1, height));

        Thickness inside = Inside;
        double top = 1 + inside.Top, blockHeight = height - 2 - inside.Top - inside.Bottom;
        var fill = (Brush?)GetValue(IsBest ? BestFillProperty : FillProperty);
        int filled = UnitStat.BlocksFor(Value, FullBar, Blocks);
        for (int block = 0; block < filled; block++)
            drawing.DrawRectangle(fill, null, snapping.Snap(1 + inside.Left + block * BlockStep, top, BlockWidth, blockHeight));

        if (Marker is double marker && FullBar > 0)
        {
            // the line sits where the pinned unit's last block would end; drawn at 80% so the blocks show through it
            int markerBlocks = (int)Math.Round(Math.Min(1, Math.Max(0, marker) / FullBar) * Blocks, MidpointRounding.AwayFromZero);
            drawing.PushOpacity(0.8);
            drawing.DrawRectangle((Brush?)GetValue(MarkerBrushProperty), null, snapping.Snap(1 + inside.Left + markerBlocks * BlockStep, 1, 2, height - 2));
            drawing.Pop();
        }
    }

    private static DependencyProperty Register(string name, Type type, object? defaultValue, bool affectsMeasure = false) =>
        DependencyProperty.Register(name, type, typeof(BlockMeter), new FrameworkPropertyMetadata(defaultValue,
            affectsMeasure ? FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.AffectsRender));
}
