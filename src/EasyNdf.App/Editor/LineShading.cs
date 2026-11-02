using System.Windows;
using System.Windows.Media;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// What is drawn behind whole lines of the File Editor: the line Find, Go to line and a search result point at, outlined
/// in the accent colour (filled, it looked the same as the selected text, which is the match itself); and in "Changes
/// only", removed and added lines in a faint red and green (15% of the theme's d-del and d-add).
/// </summary>
public sealed class LineShading : IBackgroundRenderer
{
    /// <summary>The start of the line pointed at, which follows edits; null for none.</summary>
    public TextAnchor? PointedAt { get; set; }

    /// <summary>Each line's mark in "Changes only", by line number from 0; null otherwise.</summary>
    public IReadOnlyList<LineMark>? Marks { get; set; }

    /// <summary>Drawn under the text and the selection.</summary>
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>
    /// The outline of the line pointed at, drawn on its own layer above the selection (the caret's): drawn with the
    /// backgrounds, the selected match's fill covered it. Added to the text view beside this.
    /// </summary>
    public IBackgroundRenderer Outline => outline ??= new PointedLineOutline(this);

    private PointedLineOutline? outline;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        double width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
        foreach (VisualLine line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - textView.VerticalOffset;
            if (MarkBrush(number - 1) is Brush mark)
                drawingContext.DrawRectangle(mark, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
        }
    }

    /// <summary>The outline of the line pointed at, on the caret layer: above the selection (an outline hides no letters).</summary>
    private sealed class PointedLineOutline(LineShading shading) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || shading.PointedAt is not { IsDeleted: false } anchor) return;
            if (Application.Current.Resources["accent"] is not Brush accent) return;
            double width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
            foreach (VisualLine line in textView.VisualLines)
            {
                if (line.FirstDocumentLine.LineNumber != anchor.Line) continue;
                // a one-pixel outline, on whole pixels so it stays sharp
                var pen = new Pen(accent, 1);
                pen.Freeze();
                double top = line.VisualTop - textView.VerticalOffset;
                drawingContext.DrawRectangle(null, pen, new Rect(Math.Floor(-textView.HorizontalOffset) + 0.5, Math.Floor(top) + 0.5, Math.Floor(width) - 1, Math.Floor(line.Height) - 1));
            }
        }
    }

    /// <summary>A removed or added line's background: its colour, 15% over the box (CSS <c>color-mix(..., 15%, transparent)</c>).</summary>
    private Brush? MarkBrush(int index)
    {
        if (Marks is null || index >= Marks.Count) return null;
        string? colour = Marks[index] switch { LineMark.Added => "d-add", LineMark.Removed => "d-del", _ => null };
        if (colour is null || Application.Current.Resources[colour] is not SolidColorBrush full) return null;
        var faint = new SolidColorBrush(Color.FromArgb(38, full.Color.R, full.Color.G, full.Color.B));
        faint.Freeze();
        return faint;
    }
}
