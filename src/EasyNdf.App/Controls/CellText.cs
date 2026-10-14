using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>
/// One line of a table's text that ends in "..." when it does not fit (the HTML app's <c>text-overflow:ellipsis</c>),
/// and that stays quick while its column is resized. WPF's own "..." (<see cref="TextTrimming.CharacterEllipsis"/>)
/// lays the line out again and again to find where to cut it, on every change of width: a few dozen long values cut
/// off in Details made dragging the pane beside it stutter. Here the width of the text up to each of its letters is
/// worked out once, so finding where to cut is a lookup, and the text block shown (<see cref="Shown"/>) is given the
/// text already cut, which it draws exactly as before. With "Wrap rows" on, the text wraps instead, as it did.
/// </summary>
public sealed class CellText : Decorator
{
    /// <summary>The whole text.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CellText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Whether the text ends in "..." when it does not fit (the list setting's "list-trimming").</summary>
    public static readonly DependencyProperty TextTrimmingProperty = TextBlock.TextTrimmingProperty.AddOwner(
        typeof(CellText), new FrameworkPropertyMetadata(TextTrimming.CharacterEllipsis, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Whether the text wraps onto more lines instead (the list setting's "list-wrapping").</summary>
    public static readonly DependencyProperty TextWrappingProperty = TextBlock.TextWrappingProperty.AddOwner(
        typeof(CellText), new FrameworkPropertyMetadata(TextWrapping.NoWrap, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The ellipsis WPF ends a cut text with.</summary>
    private const string Ellipsis = "…";

    /// <summary>The widths worked out for the text and font in use: the text up to each letter, and the ellipsis.</summary>
    private (string Text, Typeface Face, double Size, double[] UpTo, double EllipsisWidth)? widths;

    /// <summary>Makes a cell; its font, colour and spacing are set on <see cref="Shown"/>.</summary>
    public CellText()
    {
        Shown.TextTrimming = TextTrimming.None;
        Child = Shown;
    }

    /// <summary>The text block that draws the text: its font, colour, margin and line height are the cell's.</summary>
    public TextBlock Shown { get; } = new();

    /// <inheritdoc cref="TextProperty"/>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <inheritdoc cref="TextTrimmingProperty"/>
    public TextTrimming TextTrimming { get => (TextTrimming)GetValue(TextTrimmingProperty); set => SetValue(TextTrimmingProperty, value); }

    /// <inheritdoc cref="TextWrappingProperty"/>
    public TextWrapping TextWrapping { get => (TextWrapping)GetValue(TextWrappingProperty); set => SetValue(TextWrappingProperty, value); }

    private bool Cuts => TextTrimming != TextTrimming.None && TextWrapping == TextWrapping.NoWrap;

    /// <summary>
    /// The cell asks for no width of its own, so what it asks for never depends on how much of its text is cut: that is
    /// only decided when it is placed. Cutting while measuring made the size it asked for depend on
    /// the width it was measured at; a table that measures at one width and places at another then measured and placed
    /// it over and over, until WPF gave up on laying out the window, leaving tables and lists at stale sizes.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        Shown.TextWrapping = TextWrapping;
        Shown.TextTrimming = TextTrimming.None;
        if (!Cuts)
        {
            Show(Text);
            Shown.Measure(constraint);
            return Shown.DesiredSize;
        }
        // its height is a line's, whatever is cut; it asks for no width of its own, and fits the room its column gives
        // it (the tables it is in share their width out themselves, by their texts' widths)
        Shown.Measure(constraint);
        return new Size(0, Shown.DesiredSize.Height);
    }

    /// <summary>
    /// The text block asks for a new size whenever its cut text changes, which is when the cell is placed. What the cell
    /// asks for does not depend on that (a line's height, and no width), so it is not measured again for it: that
    /// measured the text block at the column's width, then placed it at another, back and forth, until WPF gave up on
    /// laying out the window for that frame, and the file list's headings and the panes' arrangement fell behind.
    /// </summary>
    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        if (!Cuts) base.OnChildDesiredSizeChanged(child);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Cuts)
        {
            // the text cut for the room it is placed in: its size was already given as the whole text's, so this
            // changes nothing in the layout around it
            Show(TextFor(arrangeSize.Width));
            Shown.Measure(arrangeSize);
        }
        Shown.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }

    /// <summary>Gives the text block a text, only when it is a different one, so nothing is laid out again for nothing.</summary>
    private void Show(string text)
    {
        if (Shown.Text != text) Shown.Text = text;
    }

    /// <summary>The text as it fits in a width (the cell's, its margin included): whole, or as much as fits and "...".</summary>
    private string TextFor(double cellWidth)
    {
        string text = Text;
        if (text == "" || double.IsInfinity(cellWidth)) return text;
        double room = cellWidth - Shown.Margin.Left - Shown.Margin.Right - Shown.Padding.Left - Shown.Padding.Right;
        var (_, _, _, upTo, ellipsisWidth) = WidthsNow();
        if (upTo[^1] <= room) return text;
        // the most letters that fit with the ellipsis after them, found by halving: the widths only grow
        int low = 0, high = text.Length;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (upTo[middle] + ellipsisWidth <= room) low = middle;
            else high = middle - 1;
        }
        return text[..low] + Ellipsis;
    }

    /// <summary>The widths for the text and font in use, worked out again only when one of them has changed.</summary>
    private (string Text, Typeface Face, double Size, double[] UpTo, double EllipsisWidth) WidthsNow()
    {
        string text = Text;
        var face = new Typeface(Shown.FontFamily, Shown.FontStyle, Shown.FontWeight, Shown.FontStretch);
        double size = Shown.FontSize;
        if (widths is { } known && known.Text == text && known.Face.Equals(face) && known.Size == size) return known;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Formatted(string s) => new(s, CultureInfo.CurrentUICulture, Shown.FlowDirection, face, size, Brushes.Black, pixelsPerDip);
        // the text laid out once, as the text block lays it out (fallback fonts and kerning included); its width up to
        // each letter is read from where WPF puts that letter
        FormattedText whole = Formatted(text);
        var upTo = new double[text.Length + 1];
        for (int i = 1; i <= text.Length; i++)
            upTo[i] = Math.Max(upTo[i - 1], whole.BuildHighlightGeometry(new Point(), 0, i)?.Bounds.Right ?? upTo[i - 1]);
        upTo[text.Length] = Math.Max(upTo[text.Length], whole.WidthIncludingTrailingWhitespace);
        widths = (text, face, size, upTo, Formatted(Ellipsis).WidthIncludingTrailingWhitespace);
        return widths.Value;
    }
}
