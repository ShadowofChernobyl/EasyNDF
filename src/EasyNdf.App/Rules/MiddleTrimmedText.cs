using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyNdf.App.Rules;

/// <summary>
/// A line of text that, when it does not fit, is cut in its middle rather than at its end: "Community Bal…Part 01" keeps
/// the start and the end. Mod names often share a long start ("Community Balance Overhaul Extended Part 01", "… Part
/// 02"), and cut at the end they all looked the same. With "Wrap text in lists" on (the "list-wrapping" resource), it
/// wraps onto more lines instead, as the other lists' texts do. The whole text is for the line's tooltip to show.
/// </summary>
public sealed class MiddleTrimmedText : FrameworkElement
{
    /// <summary>The text.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The space around the text.</summary>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Whether the text wraps onto more lines instead of being cut.</summary>
    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(TextWrapping.NoWrap, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The text's colour, taken from around it as a text block's is.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The font, taken from around it.</summary>
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The font size, taken from around it.</summary>
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The font weight (bold for the open mod), taken from around it.</summary>
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The font style, taken from around it.</summary>
    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <inheritdoc cref="PaddingProperty"/>
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <inheritdoc cref="TextWrappingProperty"/>
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }

    /// <inheritdoc cref="ForegroundProperty"/>
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc cref="FontFamilyProperty"/>
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <inheritdoc cref="FontWeightProperty"/>
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    /// <inheritdoc cref="FontStyleProperty"/>
    public FontStyle FontStyle { get => (FontStyle)GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }

    /// <summary>Whether the text shown is cut short.</summary>
    public bool IsCut { get; private set; }

    /// <summary>The text as drawn, cut or whole, for the width it was last given.</summary>
    private FormattedText? shown;

    /// <summary>Follows the "Wrap text in lists" setting, as the lists' other texts do.</summary>
    public MiddleTrimmedText() => SetResourceReference(TextWrappingProperty, "list-wrapping");

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Thickness padding = Padding;
        double room = Math.Max(0, availableSize.Width - padding.Left - padding.Right);
        shown = Fit(room);
        double width = TextWrapping == TextWrapping.NoWrap ? shown.WidthIncludingTrailingWhitespace : shown.Width;
        return new Size(Math.Min(width, room) + padding.Left + padding.Right, shown.Height + padding.Top + padding.Bottom);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Thickness padding = Padding;
        shown = Fit(Math.Max(0, finalSize.Width - padding.Left - padding.Right));
        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (shown is null) return;
        Thickness padding = Padding;
        // the text's own height, in the middle of the room given, as a text block centres a line
        double top = padding.Top + Math.Max(0, (RenderSize.Height - padding.Top - padding.Bottom - shown.Height) / 2);
        // the colour can change without the text being laid out again (the open mod turns the accent colour)
        shown.SetForegroundBrush(Foreground);
        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        drawingContext.DrawText(shown, new Point(padding.Left, top));
        drawingContext.Pop();
    }

    /// <summary>
    /// The text to draw in a width: whole when it fits (or wraps), else as much of its start and its end as fits around
    /// "…", the start keeping the odd character.
    /// </summary>
    private FormattedText Fit(double room)
    {
        string text = Text ?? "";
        FormattedText whole = Formatted(text);
        if (TextWrapping != TextWrapping.NoWrap)
        {
            if (!double.IsInfinity(room)) whole.MaxTextWidth = Math.Max(1, room);
            IsCut = false;
            return whole;
        }
        IsCut = whole.WidthIncludingTrailingWhitespace > room + 0.5 && text.Length > 1;
        if (!IsCut) return whole;
        // the most characters kept that still fit, found by halving
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            int keep = (low + high + 1) / 2;
            if (Formatted(Cut(text, keep)).WidthIncludingTrailingWhitespace <= room + 0.5) low = keep;
            else high = keep - 1;
        }
        return Formatted(Cut(text, low));
    }

    /// <summary>The text with only a number of its characters kept, half from its start and half from its end, "…" between.</summary>
    private static string Cut(string text, int keep)
    {
        int start = (keep + 1) / 2, end = keep - start;
        // never split a character made of two halves (an emoji)
        if (start > 0 && char.IsHighSurrogate(text[start - 1])) start--;
        if (end > 0 && char.IsLowSurrogate(text[text.Length - end])) end--;
        return text[..start].TrimEnd() + "…" + text[(text.Length - end)..].TrimStart();
    }

    /// <summary>Text laid out in the element's font and colour.</summary>
    private FormattedText Formatted(string text) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
            FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
