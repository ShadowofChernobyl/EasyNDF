using System.Windows.Documents;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.Engine.Editing;
using EasyNdf.App.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// A place in the text that cannot be read, or a value the value check marks, which moves with the edits made around
/// it until the next check.
/// </summary>
public sealed class ProblemMark : TextSegment
{
    /// <summary>What is wrong, without its line: "expected \")\" (found \"export\")".</summary>
    public required string Message { get; init; }

    /// <summary>What it does to mod generation, for a value the value check marks; empty for a syntax problem.</summary>
    public string Why { get; init; } = "";

    /// <summary>Whether it is a warning (mod generation works) rather than an error.</summary>
    public bool IsWarning { get; init; }

    /// <summary>The line it is on, from 1, as the text is now.</summary>
    public int Line(TextDocument document) => document.GetLineByOffset(Math.Min(StartOffset, document.TextLength)).LineNumber;
}

/// <summary>
/// The File Editor's check: half a second after typing stops, the text is checked away from the screen's thread, so
/// typing never waits for it: its syntax (<see cref="NdfChecker"/>), and, with a <see cref="Guide"/>, its values
/// (<see cref="ValueChecker"/>). Each problem gets a wavy line under it, in the theme's error colour (its warning
/// colour for a warning), and pointing at the line shows what is wrong. <see cref="Reveal"/> goes to a problem and
/// tints its line until the caret leaves it.
/// </summary>
public sealed partial class SyntaxProblems : IBackgroundRenderer
{
    /// <summary>How long after the last key the text is checked.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    private readonly TextEditor editor;
    private readonly DispatcherTimer waiting;
    /// <summary>
    /// The tip under the mouse: drawn as the app's tooltips are, but in a popup that stays open while the mouse is on
    /// it (as VS Code's), so its "Open in Help" link can be clicked. It closes a moment after the mouse leaves both.
    /// </summary>
    private readonly System.Windows.Controls.Primitives.Popup hoverTip = new()
    {
        Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
        AllowsTransparency = true,
        StaysOpen = true,
        Focusable = false,
    };

    /// <summary>The tip's box, with the tooltips' look.</summary>
    private readonly Controls.Bevel hoverBox = new() { Padding = new Thickness(7, 4, 7, 4), MaxWidth = 384 };

    /// <summary>Closes the tip a moment after the mouse leaves the word and the tip.</summary>
    private readonly DispatcherTimer closeTip = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(350) };
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
        editor.TextArea.TextView.MouseHoverStopped += (_, _) => closeTip.Start();
        hoverBox.SetResourceReference(Controls.Bevel.TopLeftProperty, "hi");
        hoverBox.SetResourceReference(Controls.Bevel.BottomRightProperty, "lo");
        hoverBox.SetResourceReference(Controls.Bevel.BackgroundProperty, "panel");
        hoverBox.SetResourceReference(TextElement.FontFamilyProperty, "ui-font");
        hoverBox.SetResourceReference(TextElement.ForegroundProperty, "ink");
        TextElement.SetFontSize(hoverBox, 12);
        TextBlock.SetLineHeight(hoverBox, 17);
        TextBlock.SetLineStackingStrategy(hoverBox, LineStackingStrategy.BlockLineHeight);
        TextOptions.SetTextFormattingMode(hoverBox, TextFormattingMode.Ideal);
        hoverBox.MouseEnter += (_, _) => closeTip.Stop();
        hoverBox.MouseLeave += (_, _) => closeTip.Start();
        hoverTip.Child = hoverBox;
        closeTip.Tick += (_, _) =>
        {
            closeTip.Stop();
            if (!hoverBox.IsMouseOver) hoverTip.IsOpen = false;
        };
        // typing, scrolling or leaving the window closes it at once
        editor.TextArea.PreviewKeyDown += (_, _) => CloseTip();
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => CloseTip();
        // the tip's "Open in Help" link takes the focus as it is clicked: the tip stays for its click
        editor.LostKeyboardFocus += (_, e) => { if (!hoverBox.Holds(e.NewFocus as DependencyObject)) CloseTip(); };
        editor.TextArea.Caret.PositionChanged += (_, _) => ForgetPickedLineWhenLeft();
    }

    /// <summary>Raised when the problems found change.</summary>
    public event Action? Changed;

    /// <summary>The problems found by the last check, in the order they come in the text.</summary>
    public IReadOnlyList<ProblemMark> Problems => marks is null ? [] : [.. marks];

    /// <summary>The text being checked; null when none is (an export, which cannot be edited).</summary>
    public TextDocument? Document => document;

    /// <summary>
    /// What each type's names take, to check the values too; null checks only the syntax (Steel Division 2's files, or
    /// before the guide is read). Setting it checks the text again.
    /// </summary>
    public ValueGuide? Guide
    {
        get => guide;
        set
        {
            if (ReferenceEquals(value, guide)) return;
            guide = value;
            if (document is null) return;
            marks = null; // checked whole again, for the values everywhere
            checkNumber++; // as for the impacts: a check made without the guide, still running, is thrown away
            _ = CheckNow();
        }
    }

    private ValueGuide? guide;

    /// <summary>
    /// The impact names an ammunition's ImpactHappening can take (<see cref="ImpactNames"/>); null checks none. Setting
    /// it checks the text again.
    /// </summary>
    public IReadOnlySet<string>? Impacts
    {
        get => impacts;
        set
        {
            if (ReferenceEquals(value, impacts)) return;
            impacts = value;
            if (document is null || guide is null) return;
            marks = null; // checked whole again, for the impacts everywhere
            // a check made without them, still running, is thrown away: it would keep its marks, and the next check
            // would then read only what was typed since
            checkNumber++;
            _ = CheckNow();
        }
    }

    private IReadOnlySet<string>? impacts;

    /// <summary>
    /// What the tip at a place shows, given the problem there (null for none); null shows only the problem's message.
    /// The File Editor gives the names' tips with it.
    /// </summary>
    public Func<int, ProblemMark?, object?>? TipAt { get; set; }

    /// <summary>Whether tips wait for now: while the suggestions' list is open, beside its own tip.</summary>
    public Func<bool> TipsWait { get; set; } = () => false;

    /// <summary>Checks this text from now on, starting at once; null checks nothing and clears the marks.</summary>
    public void Watch(TextDocument? text)
    {
        if (ReferenceEquals(text, document)) return;
        if (document is not null) document.Changed -= TextChanged;
        document = text;
        // another file's impact names may not be this one's: they are given again for it
        impacts = null;
        edited.Follow(text);
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

    /// <summary>
    /// Checks this text from now on with a guide to its values (null for the syntax only): a new text is checked with
    /// it from the start; the same text is checked again only when the guide changed.
    /// </summary>
    public void Watch(TextDocument? text, ValueGuide? valueGuide)
    {
        if (!ReferenceEquals(text, document)) guide = valueGuide;
        Watch(text);
        Guide = valueGuide;
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
        // a large text checked before is checked again only where it was typed in, which is quick; one to be checked whole waits longer
        if (document is not null) waiting.Interval = marks is not null ? Pause : PauseAfterTyping(document.TextLength);
        waiting.Start();
    }

    /// <summary>
    /// How long after the last key a text is read again: half a second, longer for a large text, up to five seconds
    /// for one of 100 MB and more (Steel Division 2's UniteDescriptor.ndf is 117 MB): reading one takes seconds and
    /// gigabytes, and each pause in typing started a read.
    /// </summary>
    public static TimeSpan PauseAfterTyping(int length) =>
        length < 2_000_000 ? Pause : TimeSpan.FromMilliseconds(Math.Min(5000, 1500 + length / 30_000));

    /// <summary>Where the text was typed in since it was last checked, so a large one is checked a declaration at a time.</summary>
    private readonly EditedDeclarations edited = new();

    /// <summary>Whether a check is being made, and whether the text changed while it was.</summary>
    private bool checking, checkAgain;

    /// <summary>
    /// Checks the text as it is now, away from the screen's thread, and marks what it finds. One check at a time: a
    /// check asked for while one is made waits for it, then the text is checked once more, as it is then.
    /// </summary>
    internal async Task CheckNow()
    {
        waiting.Stop();
        if (document is null) return;
        if (checking)
        {
            checkAgain = true;
            return;
        }
        checking = true;
        try
        {
            int mine = ++checkNumber;
            TextDocument checkedText = document;
            ValueGuide? valueGuide = guide;
            IReadOnlySet<string>? impactNames = impacts;
            Func<string, bool>? declaredElsewhere = DeclaredElsewhere?.Invoke();
            string wholeText = checkedText.Text;
            // a large text checked before: only the declarations typed in since are checked again; the marks of the
            // others moved with the text as it changed
            if (marks is not null && checkedText.TextLength >= EditedDeclarations.LargeText && edited.Take() is { } stretches)
            {
                int taken = edited.Count;
                List<(int Start, string Text)> parts = [.. stretches.Select(stretch => (stretch.Start, checkedText.GetText(stretch.Start, stretch.End - stretch.Start)))];
                List<(int Start, List<NdfProblem> Found, List<ValueProblem> Values)> checkedParts = await Task.Run(() =>
                    parts.Select(part => (part.Start, NdfChecker.Check(part.Text), ValuesIn(part.Text, valueGuide, impactNames, part.Start, Declared(wholeText, declaredElsewhere)))).ToList());
                if (mine != checkNumber || !ReferenceEquals(checkedText, document)) return;
                foreach ((int start, int end) in stretches)
                    foreach (ProblemMark old in marks.FindOverlappingSegments(start, end - start).ToList()) marks.Remove(old);
                foreach ((int start, List<NdfProblem> foundIn, List<ValueProblem> values) in checkedParts)
                {
                    foreach (NdfProblem problem in foundIn) marks.Add(SyntaxMark(problem, start));
                    foreach (ValueProblem problem in values) marks.Add(ValueMark(problem, checkedText));
                }
                edited.Done(taken);
                Redraw();
                Changed?.Invoke();
                return;
            }
            ITextSource snapshot = checkedText.CreateSnapshot();
            (List<NdfProblem> found, List<ValueProblem> foundValues) = await Task.Run(() =>
            {
                string text = snapshot.Text;
                return (NdfChecker.Check(text), ValuesIn(text, valueGuide, impactNames, 0, Declared(text, declaredElsewhere)));
            });
            // the text changed since, or another text is shown: the next check marks it
            if (mine != checkNumber || !ReferenceEquals(checkedText, document)) return;

            var newMarks = new TextSegmentCollection<ProblemMark>(checkedText);
            foreach (NdfProblem problem in found) newMarks.Add(SyntaxMark(problem, 0));
            foreach (ValueProblem problem in foundValues) newMarks.Add(ValueMark(problem, checkedText));
            marks = newMarks;
            edited.ReadWhole();
            Redraw();
            Changed?.Invoke();
        }
        finally
        {
            checking = false;
            if (checkAgain)
            {
                checkAgain = false;
                waiting.Start();
            }
        }
    }

    /// <summary>The values marked in a text (or a stretch of one, starting at <paramref name="shift"/>); none without a guide.</summary>
    private static List<ValueProblem> ValuesIn(string text, ValueGuide? guide, IReadOnlySet<string>? impacts, int shift, Func<string, bool>? isDeclared = null) =>
        guide is null ? [] : ValueChecker.Check(text, guide, shift, impacts, isDeclared);

    /// <summary>
    /// Gives, on the screen's thread before a check, whether a word is a name the files read declare (set by the File
    /// Editor); null marks no bare word in a number.
    /// </summary>
    public Func<Func<string, bool>?>? DeclaredElsewhere { get; set; }

    /// <summary>Whether a word is declared in the text itself or, by <paramref name="elsewhere"/>, in another file; null without <paramref name="elsewhere"/>.</summary>
    private static Func<string, bool>? Declared(string text, Func<string, bool>? elsewhere)
    {
        if (elsewhere is null) return null;
        HashSet<string>? own = null;
        return word =>
        {
            own ??= [.. DeclaredName().Matches(text).Select(match => match.Groups[1].Value)];
            return own.Contains(word) || elsewhere(word);
        };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?m)^[ \t]*(?:export[ \t]+|private[ \t]+|public[ \t]+)?(\w+)[ \t]+is\b")]
    private static partial System.Text.RegularExpressions.Regex DeclaredName();

    /// <summary>A syntax problem's mark, in a stretch of the text that starts at <paramref name="start"/>.</summary>
    private static ProblemMark SyntaxMark(NdfProblem problem, int start) =>
        new() { StartOffset = start + problem.Position, Length = problem.Length, Message = WithoutLine(problem.Message) };

    /// <summary>A marked value's mark, with what is wrong and what it does.</summary>
    private static ProblemMark ValueMark(ValueProblem problem, TextDocument text) => new()
    {
        StartOffset = problem.Position, Length = problem.Length,
        Message = ValueMessages.MessageOf(problem, text), Why = ValueMessages.WhyOf(problem), IsWarning = problem.IsWarning,
    };

    /// <summary>A problem's message without its "Line N: ", which the editor shows on its own.</summary>
    private static string WithoutLine(string message)
    {
        int colon = message.IndexOf(": ", StringComparison.Ordinal);
        return message.StartsWith("Line ", StringComparison.Ordinal) && colon > 0 ? message[(colon + 2)..] : message;
    }

    // ---- drawing ----

    /// <summary>Drawn with the selection, under the text.</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Draws the picked line's tint, then a wavy line under each problem in sight: errors in the error colour, warnings in the warning colour.</summary>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || document is null) return;
        if (Application.Current.Resources["warn"] is not SolidColorBrush warn) return;
        DrawPickedLine(textView, drawingContext, warn.Color);
        if (marks is null || marks.Count == 0) return;
        int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        var errorPen = new Pen(warn, 1);
        errorPen.Freeze();
        var warningPen = Application.Current.Resources["caution"] is SolidColorBrush caution ? new Pen(caution, 1) : errorPen;
        warningPen.Freeze();
        foreach (ProblemMark mark in marks.FindOverlappingSegments(viewStart, viewEnd - viewStart))
            foreach (Rect place in PlacesOf(textView, mark))
                drawingContext.DrawGeometry(null, mark.IsWarning ? warningPen : errorPen, WavyLine(place.Left, place.Right, place.Bottom - 1));
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
    internal static StreamGeometry WavyLine(double left, double right, double bottom)
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

    /// <summary>
    /// The mouse rests on the text: a tooltip says what is wrong there (on a wavy line), and what the name or value
    /// there is (through <see cref="TipAt"/>). It closes when the mouse moves on.
    /// </summary>
    private void ShowTipAtMouse(object sender, MouseEventArgs e)
    {
        TextView view = editor.TextArea.TextView;
        if (TipsWait() || TipContentAt(e.GetPosition(view)) is not { } content) return;
        // text alone wraps, as in a tooltip
        hoverBox.Child = content is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } : content as UIElement ?? new TextBlock { Text = content.ToString() };
        closeTip.Stop();
        hoverTip.IsOpen = false;
        hoverTip.PlacementTarget = view;
        hoverTip.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Closes the tip under the mouse, such as when its link was clicked.</summary>
    public void CloseTip()
    {
        closeTip.Stop();
        hoverTip.IsOpen = false;
    }

    /// <summary>What the tip at a point of the text (from the text view's top left) shows; null for nothing.</summary>
    private object? TipContentAt(Point point)
    {
        TextView view = editor.TextArea.TextView;
        TextDocument? shownText = editor.Document;
        if (shownText is null || !view.VisualLinesValid) return null;
        TextViewPosition? place = view.GetPosition(point + view.ScrollOffset);
        if (place is null) return null;
        int offset = shownText.GetOffset(place.Value.Location);
        // a problem with no length is pointed at on the character's room after it
        ProblemMark? problem = marks is null || !ReferenceEquals(shownText, document) ? null
            : marks.FindSegmentsContaining(offset).FirstOrDefault() ?? marks.FindSegmentsContaining(Math.Max(0, offset - 1)).FirstOrDefault(m => m.Length == 0);
        return TipAt?.Invoke(offset, problem) ?? (problem is null ? null : Capitalised(problem.Message));
    }

    /// <summary>A message with its first letter in capitals, as a sentence on its own.</summary>
    internal static string Capitalised(string message) => message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message[1..];
}
