using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>A place in the text that cannot be read, which moves with the edits made around it until the next check.</summary>
public sealed class ProblemMark : TextSegment
{
    /// <summary>What is wrong, without its line: "expected \")\" (found \"export\")".</summary>
    public required string Message { get; init; }

    /// <summary>The line it is on, from 1, as the text is now.</summary>
    public int Line(TextDocument document) => document.GetLineByOffset(Math.Min(StartOffset, document.TextLength)).LineNumber;
}

/// <summary>
/// The File Editor's syntax check: half a second after typing stops, the text is checked away from the screen's thread
/// (<see cref="NdfChecker"/>), so typing never waits for it. Each problem gets a wavy line under it, in the theme's
/// warning colour, and pointing at the line shows what is wrong. <see cref="Reveal"/> goes to a problem and tints its
/// line until the caret leaves it.
/// </summary>
public sealed class SyntaxProblems : IBackgroundRenderer
{
    /// <summary>How long after the last key the text is checked.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    private readonly TextEditor editor;
    private readonly DispatcherTimer waiting;
    private readonly ToolTip hoverTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private TextDocument? document;
    private TextSegmentCollection<ProblemMark>? marks;

    /// <summary>Counts the checks asked for, so a check that finishes after the text changed again is thrown away.</summary>
    private int checkNumber;

    /// <summary>The start of the line <see cref="Reveal"/> tinted; null for none.</summary>
    private TextAnchor? pickedLine;

    /// <summary>Starts checking nothing; <see cref="Watch"/> gives it a text.</summary>
    public SyntaxProblems(TextEditor editor)
    {
        this.editor = editor;
        waiting = new DispatcherTimer(DispatcherPriority.Background) { Interval = Pause };
        waiting.Tick += (_, _) => { waiting.Stop(); _ = CheckNow(); };
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
        editor.TextArea.TextView.MouseHover += ShowTipAtMouse;
        editor.TextArea.TextView.MouseHoverStopped += (_, _) => hoverTip.IsOpen = false;
        editor.TextArea.Caret.PositionChanged += (_, _) => ForgetPickedLineWhenLeft();
    }

    /// <summary>Raised when the problems found change.</summary>
    public event Action? Changed;

    /// <summary>The problems found by the last check, in the order they come in the text.</summary>
    public IReadOnlyList<ProblemMark> Problems => marks is null ? [] : [.. marks];

    /// <summary>The text being checked; null when none is (an export, which cannot be edited).</summary>
    public TextDocument? Document => document;

    /// <summary>Checks this text from now on, starting at once; null checks nothing and clears the marks.</summary>
    public void Watch(TextDocument? text)
    {
        if (ReferenceEquals(text, document)) return;
        if (document is not null) document.Changed -= TextChanged;
        document = text;
        marks = null;
        pickedLine = null;
        checkNumber++;
        waiting.Stop();
        if (document is not null)
        {
            document.Changed += TextChanged;
            _ = CheckNow();
        }
        Redraw();
        Changed?.Invoke();
    }

    /// <summary>Gives the text the keys.</summary>
    public void FocusText() => editor.TextArea.Focus();

    /// <summary>Puts the caret on a problem, scrolls to it, and tints its line until the caret leaves it.</summary>
    public void Reveal(ProblemMark problem)
    {
        if (document is null) return;
        int offset = Math.Min(problem.StartOffset, document.TextLength);
        DocumentLine line = document.GetLineByOffset(offset);
        pickedLine = document.CreateAnchor(line.Offset);
        editor.TextArea.Caret.Offset = offset;
        // the line goes a third of the way down, clear of the list of problems over the bottom of the text
        double top = editor.TextArea.TextView.GetVisualTopByDocumentLine(line.LineNumber);
        editor.ScrollToVerticalOffset(Math.Max(0, top - editor.TextArea.TextView.ActualHeight / 3));
        editor.TextArea.Caret.BringCaretToView();
        Redraw();
    }

    // ---- checking ----

    private void TextChanged(object? sender, DocumentChangeEventArgs e)
    {
        checkNumber++;
        waiting.Stop();
        waiting.Start();
    }

    /// <summary>Checks the text as it is now, away from the screen's thread, and marks what it finds.</summary>
    internal async Task CheckNow()
    {
        waiting.Stop();
        if (document is null) return;
        int mine = ++checkNumber;
        TextDocument checkedText = document;
        ITextSource snapshot = checkedText.CreateSnapshot();
        List<NdfProblem> found = await Task.Run(() => NdfChecker.Check(snapshot.Text));
        // the text changed since, or another text is shown: the next check marks it
        if (mine != checkNumber || !ReferenceEquals(checkedText, document)) return;

        var newMarks = new TextSegmentCollection<ProblemMark>(checkedText);
        foreach (NdfProblem problem in found)
            newMarks.Add(new ProblemMark { StartOffset = problem.Position, Length = problem.Length, Message = WithoutLine(problem.Message) });
        marks = newMarks;
        Redraw();
        Changed?.Invoke();
    }

    /// <summary>A problem's message without its "Line N: ", which the editor shows on its own.</summary>
    private static string WithoutLine(string message)
    {
        int colon = message.IndexOf(": ", StringComparison.Ordinal);
        return message.StartsWith("Line ", StringComparison.Ordinal) && colon > 0 ? message[(colon + 2)..] : message;
    }

    // ---- drawing ----

    /// <summary>Drawn with the selection, under the text.</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Draws the picked line's tint, then a wavy line under each problem in sight.</summary>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || document is null) return;
        if (Application.Current.Resources["warn"] is not SolidColorBrush warn) return;
        DrawPickedLine(textView, drawingContext, warn.Color);
        if (marks is null || marks.Count == 0) return;
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        var pen = new Pen(warn, 1);
        pen.Freeze();
        foreach (ProblemMark mark in marks.FindOverlappingSegments(viewStart, viewEnd - viewStart))
            foreach (Rect place in PlacesOf(textView, mark))
                drawingContext.DrawGeometry(null, pen, WavyLine(place.Left, place.Right, place.Bottom - 1));
    }

    /// <summary>The picked line, tinted in the warning colour at 16%, across the whole width.</summary>
    private void DrawPickedLine(TextView textView, DrawingContext drawingContext, Color warn)
    {
        if (pickedLine is not { IsDeleted: false } anchor) return;
        var tint = new SolidColorBrush(Color.FromArgb(41, warn.R, warn.G, warn.B));
        tint.Freeze();
        double width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
        foreach (VisualLine line in textView.VisualLines)
        {
            if (line.FirstDocumentLine.LineNumber > anchor.Line || line.LastDocumentLine.LineNumber < anchor.Line) continue;
            double top = line.VisualTop - textView.VerticalOffset;
            drawingContext.DrawRectangle(tint, null, new Rect(-textView.HorizontalOffset, top, width, line.Height));
        }
    }

    /// <summary>Where a problem is on the screen. A problem with no length (the text ends there) takes one character's room.</summary>
    private IEnumerable<Rect> PlacesOf(TextView textView, ProblemMark mark)
    {
        if (mark.Length > 0) return BackgroundGeometryBuilder.GetRectsForSegment(textView, mark);
        TextLocation location = document!.GetLocation(Math.Min(mark.StartOffset, document.TextLength));
        Point bottom = textView.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineBottom) - textView.ScrollOffset;
        return [new Rect(bottom.X, bottom.Y - textView.DefaultLineHeight, textView.WideSpaceWidth, textView.DefaultLineHeight)];
    }

    /// <summary>A zigzag 2 pixels high, with a peak every 4 pixels, from one side to the other.</summary>
    private static StreamGeometry WavyLine(double left, double right, double bottom)
    {
        var line = new StreamGeometry();
        using (StreamGeometryContext context = line.Open())
        {
            context.BeginFigure(new Point(left, bottom), isFilled: false, isClosed: false);
            bool up = true;
            for (double x = left + 2; x <= right; x += 2, up = !up)
                context.LineTo(new Point(x, up ? bottom - 2 : bottom), isStroked: true, isSmoothJoin: false);
        }
        line.Freeze();
        return line;
    }

    private void Redraw() => editor.TextArea.TextView.InvalidateLayer(Layer);

    /// <summary>The caret left the picked line: its tint goes.</summary>
    private void ForgetPickedLineWhenLeft()
    {
        if (pickedLine is null) return;
        if (!pickedLine.IsDeleted && editor.TextArea.Caret.Line == pickedLine.Line) return;
        pickedLine = null;
        Redraw();
    }

    // ---- what is wrong, under the mouse ----

    /// <summary>The mouse rests on a wavy line: a tooltip says what is wrong there.</summary>
    private void ShowTipAtMouse(object sender, MouseEventArgs e)
    {
        if (marks is null || document is null) return;
        TextView view = editor.TextArea.TextView;
        TextViewPosition? place = view.GetPosition(e.GetPosition(view) + view.ScrollOffset);
        if (place is null) return;
        int offset = document.GetOffset(place.Value.Location);
        // a problem with no length is pointed at on the character's room after it
        ProblemMark? problem = marks.FindSegmentsContaining(offset).FirstOrDefault()
            ?? marks.FindSegmentsContaining(Math.Max(0, offset - 1)).FirstOrDefault(m => m.Length == 0);
        if (problem is null) return;
        hoverTip.Content = Capitalised(problem.Message);
        hoverTip.PlacementTarget = view;
        hoverTip.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>A message with its first letter in capitals, as a sentence on its own.</summary>
    private static string Capitalised(string message) => message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..];
}
