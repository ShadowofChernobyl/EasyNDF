using System.Windows;
using System.Windows.Media;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// The backgrounds behind whole lines of the File Editor: the line Find, Go to line and a search result point at, in the
/// selection colour (the HTML app's <c>.cm-hit</c>); and in "Changes only", removed and added lines in a faint red and
/// green (15% of the theme's d-del and d-add), which win over the first.
/// </summary>
public sealed class LineShading : IBackgroundRenderer
{
    /// <summary>The start of the line pointed at, which follows edits; null for none.</summary>
    public TextAnchor? PointedAt { get; set; }

    /// <summary>Each line's mark in "Changes only", by line number from 0; null otherwise.</summary>
    public IReadOnlyList<LineMark>? Marks { get; set; }

    /// <summary>Drawn under the text and the selection.</summary>
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        int pointedLine = PointedAt is { IsDeleted: false } anchor ? anchor.Line : -1;
        double width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
        foreach (VisualLine line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            Brush? brush = MarkBrush(number - 1) ?? (number == pointedLine ? (Brush)Application.Current.Resources["sel"] : null);
            if (brush is null) continue;
            double top = line.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(brush, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
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
