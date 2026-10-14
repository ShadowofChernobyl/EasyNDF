using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using EasyNdf.App.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// Makes every line of an AvalonEdit editor as tall as the HTML app's editor makes them (17 pixels for 12-pixel text),
/// with the text on the baseline a web page gives it. AvalonEdit sizes lines from the font alone and has no setting for
/// it; so each line starts with an invisible object of no width, as tall as the line, standing on that baseline. A line
/// is as tall as the tallest thing in it, which is then the object.
/// </summary>
public sealed class LineHeight : VisualLineElementGenerator
{
    /// <summary>How tall each line is.</summary>
    public double Height { get; set; } = 17;

    /// <summary>
    /// Makes an editor guess the height of the lines it has not drawn yet as <see cref="Height"/>, the height every drawn
    /// line has. AvalonEdit guesses from the font, a little less; on a long file the guess then grows as lines are
    /// drawn while scrolling, and the thumb and the text jump. AvalonEdit has no setting for it, so its two private
    /// fields are set, each time it may have worked its guess out again.
    /// </summary>
    public void KeepGuessesRight(TextView view)
    {
        view.VisualLinesChanged += (_, _) => SetGuess(view, Height);
        view.DocumentChanged += (_, _) => SetGuess(view, Height);
    }

    private static readonly System.Reflection.FieldInfo? GuessField =
        typeof(TextView).GetField("defaultLineHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static readonly System.Reflection.FieldInfo? HeightTreeField =
        typeof(TextView).GetField("heightTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static void SetGuess(TextView view, double height)
    {
        // the getter works the font's guess out first, if it is due, so it does not overwrite this one later
        if (GuessField is null || HeightTreeField is null || Math.Abs(view.DefaultLineHeight - height) < 0.01) return;
        GuessField.SetValue(view, height);
        object? tree = HeightTreeField.GetValue(view);
        tree?.GetType().GetProperty("DefaultLineHeight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(tree, height);
    }

    /// <summary>The object goes at the start of every line, and nowhere else.</summary>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        return startOffset == line.Offset ? startOffset : -1;
    }

    public override VisualLineElement ConstructElement(int offset)
    {
        TextRunProperties text = CurrentContext.GlobalTextRunProperties;
        double baseline = CssTextPosition.BrowserBaseline(text.Typeface, text.FontRenderingEmSize, Height);
        return new Strut(Height, baseline);
    }

    /// <summary>The invisible object: it takes no width and no place in the text, so the caret and the mouse pass over it.</summary>
    private sealed class Strut(double height, double baseline) : VisualLineElement(1, 0)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context) => new Run(height, baseline, TextRunProperties);

        public override int GetNextCaretPosition(int visualColumn, System.Windows.Documents.LogicalDirection direction, ICSharpCode.AvalonEdit.Document.CaretPositioningMode mode) => -1;

        public override bool IsWhitespace(int visualColumn) => true;
    }

    /// <summary>The object as the text formatter sees it: no width, the line's height, the browser's baseline.</summary>
    private sealed class Run(double height, double baseline, TextRunProperties properties) : TextEmbeddedObject
    {
        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakPossible;
        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakPossible;
        public override bool HasFixedSize => true;
        public override CharacterBufferReference CharacterBufferReference => new(" ", 0);
        public override int Length => 1;
        public override TextRunProperties Properties => properties;
        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(0, height, baseline);
        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, 0, 0, height);
        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways) { }
    }
}
