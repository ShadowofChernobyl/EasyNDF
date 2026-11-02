using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EasyNdf.App.Units;

/// <summary>
/// A short text drawn as Steel Division 2 draws the marks on its unit cards (TextStyles.ndf): bold, filled with a
/// colour or a gradient, with a black outline, sized to its ink so it sits tight in the box around it.
/// </summary>
public sealed class OutlinedText : FrameworkElement
{
    /// <summary>The text, such as "25".</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(OutlinedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>What the letters are filled with.</summary>
    public Brush Fill { get; init; } = Brushes.White;

    /// <summary>The letters' size.</summary>
    public double Size { get; init; } = 11.5;

    /// <summary>The outline's width (the game's StrokeSize 0.55 at its sizes is about one pixel).</summary>
    public double OutlineWidth { get; init; } = 1.6;

    private static readonly Typeface Face = new(new FontFamily("Tahoma, Verdana"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private TextShapes.Shape Shape()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var outline = new Pen(Brushes.Black, OutlineWidth) { LineJoin = PenLineJoin.Round };
        return TextShapes.Of("outlined" + Size.ToString(CultureInfo.InvariantCulture), Text, Face.FontFamily, FontWeights.Bold, pixelsPerDip, outline, () =>
            new FormattedText(Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, Size, Brushes.White, pixelsPerDip).BuildGeometry(new Point(0, 0)));
    }

    /// <summary>As large as the drawn text with its outline.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Text == "") return new Size(0, 0);
        Rect ink = Shape().Ink;
        return new Size(ink.Width, ink.Height);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (Text == "") return;
        TextShapes.Shape shape = Shape();
        drawing.PushTransform(new TranslateTransform(-shape.Ink.X, -shape.Ink.Y));
        // the outline under the fill, so the letters keep their full width
        drawing.DrawGeometry(null, new Pen(Brushes.Black, OutlineWidth) { LineJoin = PenLineJoin.Round }, shape.Geometry);
        drawing.DrawGeometry(Fill, null, shape.Geometry);
        drawing.Pop();
    }
}
