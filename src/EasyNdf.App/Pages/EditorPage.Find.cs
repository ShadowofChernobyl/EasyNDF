using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;

namespace EasyNdf.App.Pages;

/// <summary>
/// Find, Replace and Go to line (the HTML app's <c>edFindGo</c>, <c>edReplace</c> and <c>edReveal</c>). Matching follows
/// the options in the Find box (Editor/SearchOptions.cs): Match Case, Whole Word, Regular Expression, and In Selection,
/// which keeps Find and Replace All inside the text selected when it was switched on. In the file, Find moves the
/// selection to the next match; in a read-only view it goes from line to line. The line found is marked.
/// </summary>
public partial class EditorPage
{
    private void FindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
    }

    private void PreviousClicked(object sender, RoutedEventArgs e) => FindNext(-1);

    private void NextClicked(object sender, RoutedEventArgs e) => FindNext(1);

    /// <summary>Finds the next match below (1) or above (-1), going round from the end to the start.</summary>
    internal void FindNext(int direction)
    {
        string wanted = FindBox.Text;
        if (shown is null || wanted == "") return;
        if (shown.IsFile) FindInFile(wanted, direction);
        else FindInView(wanted, direction);
    }

    /// <summary>The pattern of what is typed, or null with a warning when the regular expression cannot be read.</summary>
    private Regex? FindPattern(string wanted)
    {
        Regex? pattern = SearchOptions.Pattern(wanted, out string? problem);
        if (pattern is null) Say(Texts.Get("Editor.Find.BadPattern", ("problem", problem ?? "")), NoticeKind.Caution);
        return pattern;
    }

    /// <summary>Where Find looks in the file: the In Selection range while it is on, else the whole text.</summary>
    private (int Start, int End) FindScope()
    {
        int length = Text.Document.TextLength;
        if (scopeStart is { IsDeleted: false } from && scopeEnd is { IsDeleted: false } to && to.Offset > from.Offset)
            return (Math.Min(from.Offset, length), Math.Min(to.Offset, length));
        return (0, length);
    }

    /// <summary>In the file: from the selection, the next match is selected and its line marked, going round in the scope.</summary>
    private void FindInFile(string wanted, int direction)
    {
        if (FindPattern(wanted) is not Regex pattern) return;
        string text = Text.Text;
        (int start, int end) = FindScope();
        List<Match> matches;
        try { matches = [.. SearchOptions.Matches(pattern, text, start, end)]; }
        catch (RegexMatchTimeoutException) { Say(Texts.Get("Editor.Find.TooSlow"), NoticeKind.Caution); return; }
        if (matches.Count == 0)
        {
            Say(Texts.Get("Editor.Find.NotFound", ("text", wanted)), NoticeKind.Caution);
            return;
        }
        int after = Text.SelectionStart + Text.SelectionLength, before = Text.SelectionStart;
        Match found = direction > 0
            ? matches.FirstOrDefault(match => match.Index >= after) ?? matches[0]
            : matches.LastOrDefault(match => match.Index < before) ?? matches[^1];
        // Find looks through the whole text; a match inside a folded block opens that block
        Unfold(found.Index, found.Index + found.Length);
        Text.Select(found.Index, found.Length);
        PointAt(Text.Document.GetLineByOffset(found.Index).LineNumber - 1);
        Say(Texts.Get("Editor.Find.Count", ("at", Texts.Number(matches.IndexOf(found) + 1)), ("count", Texts.Number(matches.Count))));
        // the keyboard stays where it was, as in Sublime Text: Enter in the find box goes on to the next match, and
        // Escape gives the text the keyboard with the match selected
    }

    /// <summary>In a read-only view: from the line marked, the next line with a match is marked.</summary>
    private void FindInView(string wanted, int direction)
    {
        if (FindPattern(wanted) is not Regex pattern) return;
        TextDocument document = shown!.Document;
        int count = document.LineCount;
        int start = shown.PointedAt is { IsDeleted: false } anchor ? anchor.Line - 1 : direction > 0 ? -1 : 0;
        for (int step = 1; step <= count; step++)
        {
            int index = ((start + direction * step) % count + count) % count;
            string line = document.GetText(document.GetLineByNumber(index + 1));
            bool has;
            try { has = SearchOptions.Matches(pattern, line, 0, line.Length).Any(); }
            catch (RegexMatchTimeoutException) { Say(Texts.Get("Editor.Find.TooSlow"), NoticeKind.Caution); return; }
            if (!has) continue;
            PointAt(index);
            Say(Texts.Get("Editor.Find.Found", ("file", shown.Name), ("text", wanted), ("line", Texts.Number(index + 1))));
            return;
        }
        Say(Texts.Get("Editor.Find.NotFound", ("text", wanted)), NoticeKind.Caution);
    }

    /// <summary>
    /// Marks a line and scrolls it into the middle of the editor (the HTML app's <c>edReveal</c>). Lines count from 0.
    /// </summary>
    private void PointAt(int index)
    {
        if (shown is null) return;
        TextDocument document = shown.Document;
        DocumentLine line = document.GetLineByNumber(Math.Clamp(index + 1, 1, document.LineCount));
        Unfold(line.Offset, line.Offset);
        shown.PointedAt = document.CreateAnchor(line.Offset);
        shading.PointedAt = shown.PointedAt;
        Text.UpdateLayout();
        double top = Text.TextArea.TextView.GetVisualTopByDocumentLine(line.LineNumber);
        double middle = shown.IsFile ? top + LineHeightForSize / 2 - Text.ViewportHeight / 2 : top - Math.Floor(Text.ViewportHeight / 2);
        Text.ScrollToVerticalOffset(Math.Max(0, middle));
        Text.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
    }

    private void GoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || shown is null) return;
        e.Handled = true;
        GoToLine(GoBox.Text);
    }

    /// <summary>"Go to line": the line typed is marked and scrolled into the middle.</summary>
    internal void GoToLine(string text)
    {
        if (shown is null) return;
        string typed = text.Trim();
        if (typed == "" || !typed.All(char.IsAsciiDigit) || !int.TryParse(typed, out int number) || number < 1)
        {
            Say(Texts.Get("Editor.Find.TypeLine"), NoticeKind.Caution);
            return;
        }
        PointAt(number - 1);
    }

    private void ReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ReplaceOne();
    }

    private void ReplaceClicked(object sender, RoutedEventArgs e) => ReplaceOne();

    /// <summary>"Replace": when the selection is a match, it is replaced; then the next match is found.</summary>
    internal void ReplaceOne()
    {
        if (!CanReplace(out string wanted) || FindPattern(wanted) is not Regex pattern) return;
        // the selection is replaced when it is one whole match, as Find leaves it
        if (Text.SelectionLength > 0)
        {
            string selected = Text.SelectedText;
            Match whole = pattern.Match(selected);
            if (whole.Success && whole.Index == 0 && whole.Length == selected.Length)
            {
                int start = Text.SelectionStart;
                string with = ReplacementFor(whole);
                Text.Document.Replace(start, Text.SelectionLength, with);
                Text.Select(start + with.Length, 0);
            }
        }
        FindNext(1);
    }

    /// <summary>"Replace all": every match, as one step to undo.</summary>
    private void ReplaceAllClicked(object sender, RoutedEventArgs e) => ReplaceAll();

    internal void ReplaceAll()
    {
        if (!CanReplace(out string wanted) || FindPattern(wanted) is not Regex pattern) return;
        (int scopeFrom, int scopeTo) = FindScope();
        List<Match> matches;
        try { matches = [.. SearchOptions.Matches(pattern, Text.Text, scopeFrom, scopeTo)]; }
        catch (RegexMatchTimeoutException) { Say(Texts.Get("Editor.Find.TooSlow"), NoticeKind.Caution); return; }
        if (matches.Count == 0)
        {
            Say(Texts.Get("Editor.Find.NotFound", ("text", wanted)), NoticeKind.Caution);
            return;
        }
        // from the end, so the places of the matches still to replace do not move
        Text.Document.BeginUpdate();
        for (int k = matches.Count - 1; k >= 0; k--) Text.Document.Replace(matches[k].Index, matches[k].Length, ReplacementFor(matches[k]));
        Text.Document.EndUpdate();
        Say(Texts.CountWide("Editor.Find.Replaced", matches.Count, ("file", shown!.Name)));
    }

    /// <summary>What a match is replaced with: the Replace box's text; with Regular Expression, $1 and the like name the match's groups.</summary>
    private string ReplacementFor(Match match) => SearchOptions.UseRegex ? match.Result(ReplaceBox.Text) : ReplaceBox.Text;

    // ---- the options in the Find box ----

    /// <summary>The In Selection range: its start and end, which follow edits; null while it is off.</summary>
    private TextAnchor? scopeStart, scopeEnd;

    /// <summary>Shows the options as they are, in the Find box and the Search all files box.</summary>
    private void ShowSearchOptions()
    {
        foreach ((ToggleButton toggle, bool on) in new[] {
            (CaseToggle, SearchOptions.MatchCase), (WordToggle, SearchOptions.WholeWord), (RegexToggle, SearchOptions.UseRegex),
            (AllCaseToggle, SearchOptions.MatchCase), (AllWordToggle, SearchOptions.WholeWord), (AllRegexToggle, SearchOptions.UseRegex) })
            toggle.IsChecked = on;
        SelectionToggle.IsChecked = scopeStart is not null;
    }

    private void CaseToggled(object sender, RoutedEventArgs e) => SearchOptions.MatchCase = ((ToggleButton)sender).IsChecked == true;

    private void WordToggled(object sender, RoutedEventArgs e) => SearchOptions.WholeWord = ((ToggleButton)sender).IsChecked == true;

    private void RegexToggled(object sender, RoutedEventArgs e) => SearchOptions.UseRegex = ((ToggleButton)sender).IsChecked == true;

    /// <summary>
    /// In Selection: on, the text selected now becomes where Find and Replace All look, shaded behind its lines; off, they
    /// look through the whole file again. With nothing selected it stays off and says so.
    /// </summary>
    private void SelectionToggled(object sender, RoutedEventArgs e)
    {
        if (SelectionToggle.IsChecked == true)
        {
            if (shown is not { IsFile: true } || Text.SelectionLength == 0)
            {
                SelectionToggle.IsChecked = false;
                Say(Texts.Get("Editor.Find.SelectFirst"), NoticeKind.Caution);
                return;
            }
            scopeStart = Text.Document.CreateAnchor(Text.SelectionStart);
            scopeEnd = Text.Document.CreateAnchor(Text.SelectionStart + Text.SelectionLength);
            scopeEnd.MovementType = AnchorMovementType.AfterInsertion;
        }
        else scopeStart = scopeEnd = null;
        shading.Scope = scopeStart is null ? null : (scopeStart, scopeEnd!);
        Text.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
    }

    /// <summary>In Selection goes off when another file or view is shown, as its range belonged to the one before.</summary>
    private void ClearSearchScope()
    {
        scopeStart = scopeEnd = null;
        shading.Scope = null;
        SelectionToggle.IsChecked = false;
    }

    /// <summary>Alt+C, Alt+W, Alt+R and Alt+L switch the options while the Find bar is open.</summary>
    private bool SearchOptionKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt || FindBar.Visibility != Visibility.Visible) return false;
        ToggleButton? toggle = e.SystemKey switch { Key.C => CaseToggle, Key.W => WordToggle, Key.R => RegexToggle, Key.L => SelectionToggle, _ => null };
        if (toggle is null || !toggle.IsEnabled) return false;
        toggle.IsChecked = toggle.IsChecked != true;
        toggle.RaiseEvent(new RoutedEventArgs(ToggleButton.ClickEvent, toggle));
        return true;
    }

    /// <summary>Replacing needs the file itself, and the text to find.</summary>
    private bool CanReplace(out string wanted)
    {
        wanted = FindBox.Text;
        if (shown is not { IsFile: true }) return false;
        if (wanted != "") return true;
        Say(Texts.Get("Editor.Find.TypeFirst"), NoticeKind.Caution);
        return false;
    }
}
