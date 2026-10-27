using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Units;

/// <summary>
/// A unit's cost on its tile, as the game draws it on a unit's card (UISpecificUnitButtonView.ndf, PrixUnit): in the
/// game's font (see <see cref="UnitCountFont"/>), in the game's orange, with a solid black outline and no box. A cost
/// the open mod changed has a "*" after it in the changed values' colour (<see cref="ModMarks"/>).
/// </summary>
public sealed class UnitCostMark : FrameworkElement
{
    /// <summary>The size the game gives the cost (16), made larger for the tile as the units in each card are.</summary>
    private const double NumberSize = 18;

    /// <summary>The game's orange for the cost (Colors.ndf, JauneOrange).</summary>
    private static readonly Brush GameOrange = MakeGameOrange();

    private static readonly Pen Outline = MakeOutline();

    /// <summary>The cost, such as "45".</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(string), typeof(UnitCostMark), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The font, from <see cref="UnitCountFont.FamilyKey"/>.</summary>
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(UnitCostMark), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The font's weight, from <see cref="UnitCountFont.WeightKey"/>.</summary>
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight), typeof(FontWeight), typeof(UnitCostMark), new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The changed values' colour, from <see cref="ModMarks.ChangedColour"/>.</summary>
    public static readonly DependencyProperty ChangedBrushProperty = DependencyProperty.Register(
        nameof(ChangedBrush), typeof(Brush), typeof(UnitCostMark), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="NumberProperty"/>
    public string Number { get => (string)GetValue(NumberProperty); set => SetValue(NumberProperty, value); }

    /// <inheritdoc cref="FontFamilyProperty"/>
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <inheritdoc cref="FontWeightProperty"/>
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    /// <inheritdoc cref="ChangedBrushProperty"/>
    public Brush ChangedBrush { get => (Brush)GetValue(ChangedBrushProperty); set => SetValue(ChangedBrushProperty, value); }

    /// <summary>Whether the open mod changed the cost: a "*" is then drawn after it, in the changed values' colour.</summary>
    public bool IsChanged
    {
        get => isChanged;
        set
        {
            isChanged = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private bool isChanged;

    /// <summary>Follows the game's font and the theme's changed values' colour.</summary>
    public UnitCostMark()
    {
        SetResourceReference(FontFamilyProperty, UnitCountFont.FamilyKey);
        SetResourceReference(FontWeightProperty, UnitCountFont.WeightKey);
        SetResourceReference(ChangedBrushProperty, ModMarks.ChangedColour);
    }

    /// <summary>As large as the drawn number with its outline, and its "*".</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Rect ink = DrawnInk();
        return new Size(ink.Width, ink.Height);
    }

    /// <summary>Where the number and its "*" are drawn, outlines and all.</summary>
    private Rect DrawnInk()
    {
        Rect ink = NumberShape().Ink;
        if (IsChanged && Number != "") ink.Union(StarShape().Ink);
        return ink;
    }

    /// <summary>The number, outlined then filled, its drawn edges at the element's corner.</summary>
    protected override void OnRender(DrawingContext drawing)
    {
        TextShapes.Shape number = NumberShape();
        Rect ink = DrawnInk();
        drawing.PushTransform(new TranslateTransform(-ink.X, -ink.Y));
        // the outline under the fill, so it widens the digits outward without eating into them
        drawing.DrawGeometry(null, Outline, number.Geometry);
        drawing.DrawGeometry(GameOrange, null, number.Geometry);
        if (IsChanged && Number != "")
        {
            TextShapes.Shape star = StarShape();
            drawing.DrawGeometry(null, Outline, star.Geometry);
            drawing.DrawGeometry(ChangedBrush, null, star.Geometry);
        }
        drawing.Pop();
    }

    /// <summary>The number as a shape, in the game's font, shared with every tile showing the same number (see <see cref="TextShapes"/>).</summary>
    private TextShapes.Shape NumberShape()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return TextShapes.Of("cost", Number, FontFamily, FontWeight, pixelsPerDip, Outline, () => MakeNumberShape(pixelsPerDip));
    }

    /// <summary>The "*" after the number, as a shape, placed where the number ends.</summary>
    private TextShapes.Shape StarShape()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // made for each number, as it starts where the number ends
        return TextShapes.Of("costStar", Number, FontFamily, FontWeight, pixelsPerDip, Outline, () => MakeStarShape(pixelsPerDip));
    }

    /// <summary>Makes the "*" shape, right after the number's text.</summary>
    private Geometry MakeStarShape(double pixelsPerDip)
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var number = new FormattedText(Number, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, NumberSize, Brushes.Black, pixelsPerDip);
        var star = new FormattedText("*", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, NumberSize, Brushes.Black, pixelsPerDip);
        return star.BuildGeometry(new Point(number.WidthIncludingTrailingWhitespace, 0));
    }

    /// <summary>Makes the number's shape; empty for no number.</summary>
    private Geometry MakeNumberShape(double pixelsPerDip)
    {
        if (Number == "") return Geometry.Empty.Clone();
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var text = new FormattedText(Number, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, NumberSize, Brushes.Black, pixelsPerDip);
        return text.BuildGeometry(new Point(0, 0));
    }

    private static Brush MakeGameOrange()
    {
        var brush = new SolidColorBrush(Color.FromRgb(255, 168, 69));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// The outline for the cost (the game's TextStyleStroke0503), as wide as the units in each card have. It is solid
    /// black, though the game's is soft: a see-through one let the picture behind show through, so the same orange
    /// looked weaker on a light picture than on a dark one.
    /// </summary>
    private static Pen MakeOutline()
    {
        // twice the width that shows: half of it is under the fill
        var pen = new Pen(Brushes.Black, 2.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
