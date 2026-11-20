using System.Windows;
using System.Windows.Media;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// What is drawn behind whole lines of the File Editor: the line Find, Go to line, a search result or a descriptor
/// points at, tinted in the theme's success green as a problem's line is tinted in its error colour (16%, Paul's choice
/// over an outline); and in "Changes only", removed and added lines in a faint red and green (15% of the theme's d-del
/// and d-add).
/// </summary>
public sealed class LineShading : IBackgroundRenderer
{
    /// <summary>The start of the line pointed at, which follows edits; null for none.</summary>
    public TextAnchor? PointedAt { get; set; }

    /// <summary>Find's In Selection range, shaded behind its lines; null while it is off.</summary>
    public (TextAnchor Start, TextAnchor End)? Scope { get; set; }

    /// <summary>Each line's mark in "Changes only", by line number from 0; null otherwise.</summary>
    public IReadOnlyList<LineMark>? Marks { get; set; }

    /// <summary>Drawn under the text and the selection.</summary>
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid) return;
        double width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
        Brush? pointedTint = PointedAt is { IsDeleted: false } ? PointedTint() : null;
        Brush? scopeTint = Scope is ({ IsDeleted: false }, { IsDeleted: false }) ? Application.Current.Resources["sel"] as Brush : null;
        foreach (VisualLine line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - textView.VerticalOffset;
            if (scopeTint is not null && number >= Scope!.Value.Start.Line && number <= Scope.Value.End.Line)
                drawingContext.DrawRectangle(scopeTint, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
            if (MarkBrush(number - 1) is Brush mark)
                drawingContext.DrawRectangle(mark, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
            if (pointedTint is not null && number == PointedAt!.Line)
                drawingContext.DrawRectangle(pointedTint, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
        }
    }

    /// <summary>The pointed line's tint: the theme's success green at 16%, as a problem's line has the error colour.</summary>
    private static Brush? PointedTint()
    {
        if (Application.Current.Resources["ok"] is not SolidColorBrush green) return null;
        var tint = new SolidColorBrush(Color.FromArgb(41, green.Color.R, green.Color.G, green.Color.B));
        tint.Freeze();
        return tint;
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
