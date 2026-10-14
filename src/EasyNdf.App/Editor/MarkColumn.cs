using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// The 14-pixel column between the line numbers and the text (the HTML app's <c>.edf</c>), a pixel larger than the
/// text. It holds the folding arrows: ▾ in the dim colour beside a line that starts a block, ▸ in the accent colour
/// beside one folded; clicking one folds or unfolds the block. "Changes only" marks removed lines "−" and added lines "+"
/// in it instead, bold.
/// </summary>
public sealed class MarkColumn : AbstractMargin
{
    /// <summary>The column's width.</summary>
    private const double ColumnWidth = 14;

    /// <summary>Each line's mark, by line number from 0; null when no line is marked.</summary>
    public IReadOnlyList<LineMark>? Marks { get; set; }

    /// <summary>A line's folding arrow, by line number from 0; null while there are none ("Changes only").</summary>
    public Func<int, FoldArrow>? Arrows { get; set; }

    /// <summary>Whether an arrow turns accent under the mouse, as in a read-only view.</summary>
    public bool ArrowsLightUp { get; set; }

    /// <summary>Raised when an arrow is clicked, with its line's number from 0.</summary>
    public event Action<int>? ArrowClicked;

    /// <summary>The line under the mouse, from 0; -1 for none.</summary>
    private int lineUnderMouse = -1;

    /// <summary>The height of a line, which the marks are centred in.</summary>
    public double LineHeight { get; set; } = 17;

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Redraw;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Redraw;
    }

    private void Redraw(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        TextView? view = TextView;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (view is null || !view.VisualLinesValid) return;
        foreach (VisualLine line in view.VisualLines)
        {
            int index = line.FirstDocumentLine.LineNumber - 1;
            (string symbol, string colour, double opacity, bool bold) = SymbolOf(index);
            if (symbol == "") continue;
            var face = new Typeface((FontFamily)GetValue(TextElement.FontFamilyProperty), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            double size = (double)GetValue(TextElement.FontSizeProperty) + 1;
            var brush = (Brush)Application.Current.Resources[colour];
            var text = new FormattedText(symbol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double top = line.VisualTop - view.VerticalOffset;
            double baseline = CssTextPosition.BrowserBaseline(face, size, LineHeight);
            drawing.PushOpacity(opacity);
            drawing.DrawText(text, new Point((ColumnWidth - text.WidthIncludingTrailingWhitespace) / 2, top + baseline - text.Baseline));
            drawing.Pop();
        }
    }

    /// <summary>What a line shows in the column: its mark, or its folding arrow; "" for nothing.</summary>
    private (string Symbol, string Colour, double Opacity, bool Bold) SymbolOf(int index)
    {
        if (Marks is not null)
        {
            if (index >= Marks.Count) return ("", "", 1, false);
            return Marks[index] switch
            {
                LineMark.Added => ("+", "d-add", 1, true),
                LineMark.Removed => ("−", "d-del", 1, true),
                _ => ("", "", 1, false),
            };
        }
        return (Arrows?.Invoke(index) ?? FoldArrow.None) switch
        {
            FoldArrow.Shut => ("▸", "accent", 1, false),
            FoldArrow.Open when ArrowsLightUp && index == lineUnderMouse => ("▾", "accent", 1, false),
            FoldArrow.Open => ("▾", "muted", 0.8, false),
            _ => ("", "", 1, false),
        };
    }

    /// <summary>The line at a point of the column, from 0; -1 for none.</summary>
    private int LineAt(Point point)
    {
        TextView? view = TextView;
        if (view is null || !view.VisualLinesValid) return -1;
        VisualLine? line = view.GetVisualLineFromVisualTop(point.Y + view.VerticalOffset);
        return line is null ? -1 : line.FirstDocumentLine.LineNumber - 1;
    }

    private bool HasArrow(int index) => index >= 0 && Marks is null && (Arrows?.Invoke(index) ?? FoldArrow.None) != FoldArrow.None;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = LineAt(e.GetPosition(this));
        Cursor = HasArrow(index) ? Cursors.Hand : null;
        if (index == lineUnderMouse) return;
        lineUnderMouse = index;
        if (ArrowsLightUp) InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        lineUnderMouse = -1;
        if (ArrowsLightUp) InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int index = LineAt(e.GetPosition(this));
        if (!HasArrow(index)) return;
        e.Handled = true;
        ArrowClicked?.Invoke(index);
    }
}

/// <summary>A line's folding arrow: none, ▾ for a block that can fold, ▸ for one folded.</summary>
public enum FoldArrow
{
    /// <summary>The line starts no block.</summary>
    None,

    /// <summary>The line starts a block, which is shown.</summary>
    Open,

    /// <summary>The line's block is folded.</summary>
    Shut,
}
