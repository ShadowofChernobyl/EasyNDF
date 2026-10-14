using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// The line numbers at the left of the File Editor, as the HTML app's editor draws them: in the dim colour, at the right
/// of a column at least three digits wide with 4 pixels before the numbers, each on the baseline of its line's text.
/// A read-only view (an export, or its changed lines) may number its lines its own way, in a column as wide as its digits.
/// </summary>
public sealed class LineNumbers : AbstractMargin
{
    /// <summary>The space before the numbers.</summary>
    private const double PaddingLeft = 4;

    private double digitWidth;

    /// <summary>Each line's number as shown, from its number counting from 1; null to show that number itself.</summary>
    public Func<int, string>? NumberOf { get; set; }

    /// <summary>
    /// How many digits wide the column is in a read-only view (the HTML app's <c>--gut</c>), with no three-digit minimum;
    /// null for the editable text's column.
    /// </summary>
    public int? Digits { get; set; }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Redraw;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Redraw;
        InvalidateMeasure();
    }

    private void Redraw(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>The numbers' typeface and size: the text's own.</summary>
    private (Typeface Face, double Size) Font()
    {
        var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        return (face, (double)GetValue(TextElement.FontSizeProperty));
    }

    private FormattedText Number(string text)
    {
        (Typeface face, double size) = Font();
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size,
            (Brush)Application.Current.Resources["muted"], VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        digitWidth = Number("0").WidthIncludingTrailingWhitespace;
        if (Digits is int viewDigits) return new Size(PaddingLeft + viewDigits * digitWidth, 0);
        int digits = (Document?.LineCount ?? 1).ToString(CultureInfo.InvariantCulture).Length;
        // min-width:3ch, with the padding inside it
        return new Size(Math.Max(3 * digitWidth, PaddingLeft + digits * digitWidth), 0);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        TextView? view = TextView;
        if (view is null || !view.VisualLinesValid) return;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        foreach (VisualLine line in view.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            string shown = NumberOf?.Invoke(lineNumber) ?? lineNumber.ToString(CultureInfo.InvariantCulture);
            if (shown == "") continue;
            FormattedText number = Number(shown);
            double top = line.VisualTop - view.VerticalOffset;
            double baseline = line.TextLines.Count > 0 ? line.TextLines[0].Baseline : number.Baseline;
            drawing.DrawText(number, new Point(RenderSize.Width - number.WidthIncludingTrailingWhitespace, top + baseline - number.Baseline));
        }
    }
}
