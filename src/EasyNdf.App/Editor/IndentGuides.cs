using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// Scope lines, as Sublime Text draws them: a thin line down each level of indentation, so the blocks of a descriptor
/// can be followed by eye. A level is four columns (a tab counts as four). They are drawn in the theme's dim text colour,
/// made faint, while the "Scope lines" setting is on.
/// </summary>
public sealed class IndentGuides : IBackgroundRenderer
{
    /// <summary>The columns one level of indentation takes.</summary>
    private const int LevelWidth = 4;

    /// <summary>How far a blank line looks above and below it for the indentation it continues.</summary>
    private const int BlankLinesLookedThrough = 40;

    /// <summary>Drawn under the text and the selection.</summary>
    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Whether the "Scope lines" setting is on.</summary>
    public static bool IsOn => AppData.Current.Settings["indentGuides"]?.GetValue<bool>() != false;

    /// <inheritdoc/>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!IsOn || !textView.VisualLinesValid || Application.Current.Resources["muted"] is not SolidColorBrush muted) return;
        // the dim text colour at a third of its strength: easy to follow, quieter than the text
        var faint = new SolidColorBrush(Color.FromArgb(85, muted.Color.R, muted.Color.G, muted.Color.B));
        var pen = new Pen(faint, 1);
        pen.Freeze();
        double columnWidth = textView.WideSpaceWidth;
        TextDocument document = textView.Document;
        foreach (VisualLine line in textView.VisualLines)
        {
            int levels = LevelsOf(document, line.FirstDocumentLine);
            // whole pixels at both ends, so the lines of two rows meet without a gap
            double top = Math.Round(line.VisualTop - textView.VerticalOffset);
            double bottom = Math.Round(line.VisualTop + line.Height - textView.VerticalOffset);
            for (int level = 0; level < levels; level++)
            {
                // in the middle of the level's first column, on a whole pixel so the line stays sharp
                double x = Math.Round((level * LevelWidth + 0.5) * columnWidth - textView.HorizontalOffset) + 0.5;
                drawingContext.DrawLine(pen, new Point(x, top), new Point(x, bottom));
            }
        }
    }

    /// <summary>
    /// The levels of indentation a line has a scope line for: those its text is indented past. A blank line takes the
    /// smaller of the lines around it, so the lines run on through gaps in a block.
    /// </summary>
    private static int LevelsOf(TextDocument document, DocumentLine line)
    {
        int? own = IndentOf(document, line);
        if (own is int columns) return (columns + LevelWidth - 1) / LevelWidth;
        int above = NearestIndent(document, line, -1), below = NearestIndent(document, line, +1);
        return (Math.Min(above, below) + LevelWidth - 1) / LevelWidth;
    }

    /// <summary>The indentation of the nearest line above or below that has text; 0 when there is none close by.</summary>
    private static int NearestIndent(TextDocument document, DocumentLine line, int direction)
    {
        DocumentLine? next = line;
        for (int step = 0; step < BlankLinesLookedThrough; step++)
        {
            next = direction < 0 ? next.PreviousLine : next.NextLine;
            if (next is null) return 0;
            if (IndentOf(document, next) is int columns) return columns;
        }
        return 0;
    }

    /// <summary>The columns of white space a line starts with; null for a line with only white space.</summary>
    private static int? IndentOf(TextDocument document, DocumentLine line)
    {
        int columns = 0;
        for (int offset = line.Offset; offset < line.EndOffset; offset++)
        {
            char c = document.GetCharAt(offset);
            if (c == ' ') columns++;
            else if (c == '\t') columns += LevelWidth - columns % LevelWidth;
            else return columns;
        }
        return null;
    }
}
