using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// One line of text with extra space after every letter, like CSS <c>letter-spacing</c>, which WPF's TextBlock cannot do.
/// The HTML app uses it for the name in the title row.
/// </summary>
public sealed class SpacedText : FrameworkElement
{
    /// <summary>The text.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SpacedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The extra space after each letter, as a share of the font size (CSS "0.04em" is 0.04).</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(SpacedText), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <inheritdoc cref="SpacingProperty"/>
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    // The font comes from the surrounding text settings, as it would for a TextBlock.
    static SpacedText()
    {
        foreach (DependencyProperty font in new[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty, TextElement.ForegroundProperty })
            font.OverrideMetadata(typeof(SpacedText), new FrameworkPropertyMetadata { Inherits = true, AffectsMeasure = true, AffectsRender = true });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        foreach (FormattedText letter in Letters()) width += letter.WidthIncludingTrailingWhitespace + SpaceAfterLetter;
        return new Size(width, LineHeight);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        double x = 0;
        foreach (FormattedText letter in Letters())
        {
            drawing.DrawText(letter, new Point(x, (LineHeight - letter.Height) / 2));
            x += letter.WidthIncludingTrailingWhitespace + SpaceAfterLetter;
        }
    }

    private double FontSize => (double)GetValue(TextElement.FontSizeProperty);

    private double SpaceAfterLetter => Spacing * FontSize;

    /// <summary>The height of a line, as the surrounding TextBlocks use.</summary>
    private double LineHeight
    {
        get
        {
            double lineHeight = (double)GetValue(System.Windows.Controls.TextBlock.LineHeightProperty);
            return double.IsNaN(lineHeight) ? FontSize * 1.2 : lineHeight;
        }
    }

    private IEnumerable<FormattedText> Letters()
    {
        var typeface = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal,
            (FontWeight)GetValue(TextElement.FontWeightProperty), FontStretches.Normal);
        var brush = (Brush)GetValue(TextElement.ForegroundProperty);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (char letter in Text)
            yield return new FormattedText(letter.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, brush, pixelsPerDip);
    }
}
