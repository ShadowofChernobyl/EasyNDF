using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.Data.FileList;
using EasyNdf.Engine.Rules;
using EasyNdf.App.Text;

namespace EasyNdf.App.Files;

/// <summary>
/// One of Details' two lists. Only the lines in sight are made, as in the file list: a run can list thousands of lines,
/// each wrapping to the width it gets, and making them all held the window up while a pane beside them was dragged.
/// Tuned for speed: Keep it that way when tidying.
/// </summary>
internal sealed class DetailsRowList : ItemsControl
{
    /// <summary>Makes an item's line, when it comes into sight.</summary>
    public Func<object, FrameworkElement>? LineOf { get; set; }

    /// <summary>Makes the list.</summary>
    public DetailsRowList() => SetResourceReference(StyleProperty, "DetailsLines");

    /// <summary>The scrolling part of the list, once the list is shown.</summary>
    public ScrollViewer? Scroller
    {
        get
        {
            ApplyTemplate();
            return Template?.FindName("Scroller", this) as ScrollViewer;
        }
    }

    /// <summary>Gives the list new lines, starting at the top or keeping its place.</summary>
    public void ShowLines(System.Collections.IEnumerable lines, bool fromTheTop)
    {
        ScrollViewer? scroller = Scroller;
        double place = scroller?.VerticalOffset ?? 0;
        ItemsSource = lines;
        if (scroller is null) return;
        if (fromTheTop) scroller.ScrollToTop();
        else scroller.ScrollToVerticalOffset(place);
    }

    /// <summary>Scrolls a line into sight.</summary>
    public void Reveal(int index) => Pages.RulesPage.BringLineIntoView(this, index);

    /// <summary>The lines made so far, in sight or just out of it.</summary>
    public IEnumerable<FrameworkElement> LinesMade()
    {
        for (int index = 0; index < Items.Count; index++)
            if (ItemContainerGenerator.ContainerFromIndex(index) is Decorator { Child: FrameworkElement line }) yield return line;
    }

    /// <summary>The line of an item, when it is made.</summary>
    public FrameworkElement? LineAt(int index) => ItemContainerGenerator.ContainerFromIndex(index) is Decorator { Child: FrameworkElement line } ? line : null;

    protected override bool IsItemItsOwnContainerOverride(object item) => false;

    protected override DependencyObject GetContainerForItemOverride() => new Decorator();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item) => ((Decorator)element).Child = LineOf?.Invoke(item);

    protected override void ClearContainerForItemOverride(DependencyObject element, object item) => ((Decorator)element).Child = null;
}

/// <summary>Text as Details writes it: in theme colours, with what Find found marked, wrapping where it reads best.</summary>
internal static class DetailsText
{
    /// <summary>A place a long name or value may wrap, which shows nothing.</summary>
    private const char SoftBreak = '​';

    /// <summary>
    /// Lets a long value wrap after its own punctuation. Without this WPF cuts it wherever the line ends, in the middle
    /// of a word.
    /// </summary>
    public static string Soft(string text)
    {
        if (text.AsSpan().IndexOfAny(",([=/") < 0) return text;
        var built = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            built.Append(c);
            if (c is ',' or '(' or '[' or '=' or '/') built.Append(SoftBreak);
        }
        return built.ToString();
    }

    /// <summary>A piece of text in a theme colour; null for the colour around it.</summary>
    public static Run Piece(string text, string? colour)
    {
        var run = new Run(text);
        if (colour is not null) run.SetResourceReference(TextElement.ForegroundProperty, colour);
        return run;
    }

    /// <summary>Adds text in a theme colour, with each place Find found marked.</summary>
    public static void Add(InlineCollection inlines, string text, string? colour, string find)
    {
        int found = find == "" ? -1 : text.IndexOf(find, StringComparison.OrdinalIgnoreCase);
        if (found < 0)
        {
            inlines.Add(Piece(Soft(text), colour));
            return;
        }
        int from = 0;
        while (found >= 0)
        {
            if (found > from) inlines.Add(Piece(Soft(text[from..found]), colour));
            Run mark = Piece(text[found..(found + find.Length)], colour);
            mark.SetResourceReference(TextElement.BackgroundProperty, "hover");
            inlines.Add(mark);
            from = found + find.Length;
            found = text.IndexOf(find, from, StringComparison.OrdinalIgnoreCase);
        }
        if (from < text.Length) inlines.Add(Piece(Soft(text[from..]), colour));
    }

    /// <summary>A wrapping line of text in the app's font.</summary>
    public static TextBlock Line(string? colour = null)
    {
        var line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, colour ?? "ink");
        return line;
    }

    /// <summary>A wrapping line of text in the code font, as names and values are written.</summary>
    public static TextBlock Code(string? colour = null)
    {
        TextBlock line = Line(colour);
        line.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        line.FontSize = 12;
        line.LineHeight = 17;
        return line;
    }

    /// <summary>A value's pieces: what changed in its colour, what stayed the same dimmed, and a mark where some was left out.</summary>
    public static void AddValue(InlineCollection inlines, IReadOnlyList<ValuePiece> pieces, int charactersLeftOut, string changedColour, string find)
    {
        foreach (ValuePiece piece in pieces)
        {
            if (piece.Kind == ValuePieceKind.LeftOut)
            {
                Run mark = Piece(piece.Text, "muted");
                mark.ToolTip = Texts.Get("Files.Details.RestUnchanged.Tip");
                inlines.Add(mark);
            }
            else Add(inlines, piece.Text, piece.Kind == ValuePieceKind.Changed ? changedColour : "muted", find);
        }
        if (charactersLeftOut <= 0) return;
        Run more = Note(" " + Texts.Get("Files.Details.MoreCharacters", ("count", Texts.Number(charactersLeftOut))));
        more.ToolTip = Texts.Get("Files.Details.MoreCharacters.Tip");
        inlines.Add(more);
    }

    /// <summary>Words about a value among its text: in the app's font, in the caution colour.</summary>
    public static Run Note(string text)
    {
        Run run = Piece(text, "caution");
        run.SetResourceReference(TextElement.FontFamilyProperty, "ui-font");
        run.FontSize = 11.5;
        return run;
    }
}

/// <summary>What Details writes for a rule's counts.</summary>
internal static class DetailsCounts
{
    /// <summary>"1 edit", "2 edits".</summary>
    public static string Edits(int count) => Texts.CountWide("Files.Details.Edits.Count", count);

    /// <summary>"1 skipped".</summary>
    public static string Skipped(int count) => Texts.CountWide("Files.Details.Skipped.Count", count);

    /// <summary>"1 descriptor matched".</summary>
    public static string Matched(int count) => Texts.CountWide("Files.Details.Matched.Count", count);

    /// <summary>"1 descriptor".</summary>
    public static string Descriptors(int count) => Texts.CountWide("Files.Details.Descriptors.Count", count);

    /// <summary>The tip of a rule's line: its kind and every count.</summary>
    public static string RuleTip(RuleStats rule)
    {
        string kind = Rules.RuleKindNames.Of(rule.Kind);
        if (RunDetails.MakesNoEdits(rule.Kind)) return kind;
        return Texts.Get("Files.Details.RuleTip", ("kind", kind), ("matched", Matched(rule.Matched)), ("edits", Edits(rule.AllEdits)), ("skipped", Skipped(rule.Skipped)));
    }

    /// <summary>"2 errors".</summary>
    public static string Errors(int count) => Texts.CountWide("Files.Run.ValueErrors.Count", count);

    /// <summary>
    /// What values do: "2 errors would stop mod generation.", "34 errors would crash the game.", or both, the errors
    /// that stop generation first.
    /// </summary>
    public static string WhatErrorsDo(IReadOnlyCollection<RunValueError> errors)
    {
        int crashing = errors.Count(error => error.Error.Problem.CrashesGame), stopping = errors.Count - crashing;
        string stop = stopping > 0 ? Texts.Get("Files.Details.ValueErrors", ("errors", Errors(stopping))) : "";
        string crash = crashing > 0 ? Texts.Get("Files.Details.ValueErrors.Crash", ("errors", Errors(crashing))) : "";
        return stop == "" ? crash : crash == "" ? stop : stop + " " + crash;
    }

    /// <summary>
    /// What a file held back for its values says: "Not exported: 2 errors would stop mod generation.", "Not exported: 34
    /// errors would crash the game.", or the first followed by what the others do.
    /// </summary>
    public static string NotExportedFor(IReadOnlyCollection<RunValueError> errors)
    {
        int crashing = errors.Count(error => error.Error.Problem.CrashesGame), stopping = errors.Count - crashing;
        if (stopping == 0) return Texts.Get("Files.Details.ValueErrors.Held.Crash", ("errors", Errors(crashing)));
        string held = Texts.Get("Files.Details.ValueErrors.Held", ("errors", Errors(stopping)));
        return crashing == 0 ? held : held + " " + Texts.Get("Files.Details.ValueErrors.Crash", ("errors", Errors(crashing)));
    }

    /// <summary>
    /// Where a value that would stop mod generation is, and what is wrong with it: "Ammo_X, line 120: AngleDispersion
    /// must be from 0 to 0.785398 (45°)."; an unnamed descriptor's starts with the line.
    /// </summary>
    public static string ErrorLine(RunValueError error)
    {
        string message = Editor.ValueMessages.MessageOf(error.Error.Problem, error.Error.FirstLine);
        string line = Texts.Number(error.Error.Line);
        return error.Error.Descriptor == ""
            ? Texts.Get("Files.Details.ValueError.Line", ("line", line), ("message", message))
            : Texts.Get("Files.Details.ValueError.At", ("descriptor", error.Error.Descriptor), ("line", line), ("message", message));
    }

    /// <summary>The tip of a value that would stop mod generation: what is wrong, in full, and what it does.</summary>
    public static string ErrorTip(RunValueError error) =>
        Editor.ValueMessages.MessageOf(error.Error.Problem, error.Error.FirstLine) + " " + Editor.ValueMessages.WhyOf(error.Error.Problem);
}
