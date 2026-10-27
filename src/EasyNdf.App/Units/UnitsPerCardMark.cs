using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Units;

/// <summary>
/// A division's units in each card, "×4", as the game draws it on a unit's card: the × smaller than the number, in the
/// game's font (see <see cref="UnitCountFont"/>), outlined in black, on a see-through box of the theme's box colour.
/// The box is fitted to the drawn number itself rather than to the font's line height, which leaves uneven room above
/// and below digits.
/// </summary>
public sealed class UnitsPerCardMark : FrameworkElement
{
    /// <summary>The sizes the game gives the × and the number (UISpecificUnitButtonView.ndf, NbUnitInThePack), made larger for the tile.</summary>
    private const double CrossSize = 15, NumberSize = 22;

    /// <summary>The room around the number inside the box, and the box's corner radius (the game's is 2).</summary>
    private const double SidePadding = 3, TopPadding = 2, CornerRadius = 2;

    /// <summary>How see-through the box is: the picture shows through it, as in the game.</summary>
    private const double BoxOpacity = 0.7;

    private static readonly Pen Outline = MakeOutline();

    /// <summary>The number, such as "4", or "2 / 3" for a unit whose number changes with the game's phases.</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(string), typeof(UnitsPerCardMark), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The font, from <see cref="UnitCountFont.FamilyKey"/>.</summary>
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(UnitsPerCardMark), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The font's weight, from <see cref="UnitCountFont.WeightKey"/>.</summary>
    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight), typeof(FontWeight), typeof(UnitsPerCardMark), new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The number's colour: the theme's success colour, or the headings' colour when the open mod changed it.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(UnitsPerCardMark), new FrameworkPropertyMetadata(Brushes.LightGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The box's colour: the theme's box colour.</summary>
    public static readonly DependencyProperty BoxProperty = DependencyProperty.Register(
        nameof(Box), typeof(Brush), typeof(UnitsPerCardMark), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="NumberProperty"/>
    public string Number { get => (string)GetValue(NumberProperty); set => SetValue(NumberProperty, value); }

    /// <inheritdoc cref="FontFamilyProperty"/>
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <inheritdoc cref="FontWeightProperty"/>
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc cref="BoxProperty"/>
    public Brush Box { get => (Brush)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }

    /// <summary>Follows the app's font, colours and theme, as the other marks do.</summary>
    public UnitsPerCardMark()
    {
        SetResourceReference(FontFamilyProperty, UnitCountFont.FamilyKey);
        SetResourceReference(FontWeightProperty, UnitCountFont.WeightKey);
        SetResourceReference(ForegroundProperty, "ok");
        SetResourceReference(BoxProperty, "well");
    }

    /// <summary>As large as the box: the drawn number with its room around it.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Rect box = BoxAround(NumberShape());
        return new Size(box.Width, box.Height);
    }

    /// <summary>The box, then the number on it, filled and outlined.</summary>
    protected override void OnRender(DrawingContext drawing)
    {
        TextShapes.Shape number = NumberShape();
        Rect box = BoxAround(number);
        // the box's corner is the element's: the number moves by as much
        var shift = new TranslateTransform(-box.X, -box.Y);
        drawing.PushOpacity(BoxOpacity);
        drawing.DrawRoundedRectangle(Box, null, new Rect(0, 0, box.Width, box.Height), CornerRadius, CornerRadius);
        drawing.Pop();
        drawing.PushTransform(shift);
        // the outline under the fill, so it widens the digits outward without eating into them
        drawing.DrawGeometry(null, Outline, number.Geometry);
        drawing.DrawGeometry(Foreground, null, number.Geometry);
        drawing.Pop();
    }

    /// <summary>The × and the number as one shape, shared with every tile showing the same number (see <see cref="TextShapes"/>).</summary>
    private TextShapes.Shape NumberShape()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return TextShapes.Of("unitsPerCard", Number, FontFamily, FontWeight, pixelsPerDip, Outline, () => MakeNumberShape(pixelsPerDip));
    }

    /// <summary>
    /// Makes the × and the number as one shape, side by side on one baseline, the × a little smaller, as the game draws
    /// them; empty for no number.
    /// </summary>
    private Geometry MakeNumberShape(double pixelsPerDip)
    {
        if (Number == "") return Geometry.Empty.Clone();
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        FormattedText cross = Text("×", typeface, CrossSize, pixelsPerDip);
        FormattedText number = Text(Number, typeface, NumberSize, pixelsPerDip);
        // the two on the number's baseline, with a small gap between them
        var shape = new GeometryGroup();
        shape.Children.Add(cross.BuildGeometry(new Point(0, number.Baseline - cross.Baseline)));
        shape.Children.Add(number.BuildGeometry(new Point(cross.WidthIncludingTrailingWhitespace + 1, 0)));
        return shape;
    }

    private static FormattedText Text(string text, Typeface typeface, double size, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, pixelsPerDip);

    /// <summary>The box around the drawn shape and its outline, with even room on each side.</summary>
    private static Rect BoxAround(TextShapes.Shape shape)
    {
        if (shape.Geometry.IsEmpty()) return new Rect(0, 0, 0, 0);
        Rect ink = shape.Ink;
        ink.Inflate(SidePadding, TopPadding);
        return ink;
    }

    /// <summary>The game's black outline (LabelUnitNameStroke), with round corners so the digits' points do not spike.</summary>
    private static Pen MakeOutline()
    {
        // twice the width that shows: half of it is under the fill
        var pen = new Pen(Brushes.Black, 2.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
