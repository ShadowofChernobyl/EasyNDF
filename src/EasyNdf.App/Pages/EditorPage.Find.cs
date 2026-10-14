using System.Globalization;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Pages;

/// <summary>
/// Find, Replace and Go to line (the HTML app's <c>edFindGo</c>, <c>edReplace</c> and <c>edReveal</c>). Matching takes
/// no account of capitals. In the file, Find moves the selection to the next match; in a read-only view it goes from
/// line to line. The line found is marked.
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

    /// <summary>In the file: from the selection, the next match is selected and its line marked.</summary>
    private void FindInFile(string wanted, int direction)
    {
        string text = Text.Text;
        int from = direction > 0 ? Text.SelectionStart + Text.SelectionLength : Math.Max(0, Text.SelectionStart - 1);
        int found = direction > 0 ? text.IndexOf(wanted, Math.Min(from, text.Length), StringComparison.OrdinalIgnoreCase) : LastMatchStartingBy(text, wanted, from);
        if (found < 0) found = direction > 0 ? text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) : LastMatchStartingBy(text, wanted, text.Length);
        if (found < 0)
        {
            Say("\"" + wanted + "\" was not found.", "caution");
            return;
        }
        // Find looks through the whole text; a match inside a folded block opens that block
        Unfold(found, found + wanted.Length);
        Text.Select(found, wanted.Length);
        PointAt(Text.Document.GetLineByOffset(found).LineNumber - 1);
        Text.TextArea.Focus();
    }

    /// <summary>The last match that starts at or before a place (JavaScript's <c>lastIndexOf</c>); -1 for none.</summary>
    private static int LastMatchStartingBy(string text, string wanted, int start)
    {
        if (text.Length == 0 || wanted.Length > text.Length) return -1;
        int lastEnd = Math.Min(text.Length - 1, start + wanted.Length - 1);
        return text.LastIndexOf(wanted, lastEnd, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>In a read-only view: from the line marked, the next line with a match is marked.</summary>
    private void FindInView(string wanted, int direction)
    {
        TextDocument document = shown!.Document;
        int count = document.LineCount;
        int start = shown.PointedAt is { IsDeleted: false } anchor ? anchor.Line - 1 : direction > 0 ? -1 : 0;
        for (int step = 1; step <= count; step++)
        {
            int index = ((start + direction * step) % count + count) % count;
            if (!document.GetText(document.GetLineByNumber(index + 1)).Contains(wanted, StringComparison.OrdinalIgnoreCase)) continue;
            PointAt(index);
            Say(shown.Name + ": \"" + wanted + "\" found on line " + (index + 1).ToString("N0", CultureInfo.InvariantCulture) + ".");
            return;
        }
        Say("\"" + wanted + "\" was not found.", "caution");
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
            Say("Type a line number, then Enter.", "caution");
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
        if (!CanReplace(out string wanted)) return;
        if (Text.SelectionLength > 0 && string.Equals(Text.SelectedText, wanted, StringComparison.OrdinalIgnoreCase))
        {
            int start = Text.SelectionStart;
            Text.Document.Replace(start, Text.SelectionLength, ReplaceBox.Text);
            Text.Select(start + ReplaceBox.Text.Length, 0);
        }
        FindNext(1);
    }

    /// <summary>"Replace all": every match, as one step to undo.</summary>
    private void ReplaceAllClicked(object sender, RoutedEventArgs e) => ReplaceAll();

    internal void ReplaceAll()
    {
        if (!CanReplace(out string wanted)) return;
        string text = Text.Text;
        var matches = new List<int>();
        for (int at = text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(wanted, at + wanted.Length, StringComparison.OrdinalIgnoreCase))
            matches.Add(at);
        if (matches.Count == 0)
        {
            Say("\"" + wanted + "\" was not found.", "caution");
            return;
        }
        // from the end, so the places of the matches still to replace do not move
        Text.Document.BeginUpdate();
        for (int k = matches.Count - 1; k >= 0; k--) Text.Document.Replace(matches[k], wanted.Length, ReplaceBox.Text);
        Text.Document.EndUpdate();
        string count = matches.Count.ToString("N0", CultureInfo.InvariantCulture) + (matches.Count == 1 ? " match" : " matches");
        Say(shown!.Name + ": replaced " + count + ". Ctrl+Z in the text undoes it.");
    }

    /// <summary>Replacing needs the file itself, and the text to find.</summary>
    private bool CanReplace(out string wanted)
    {
        wanted = FindBox.Text;
        if (shown is not { IsFile: true }) return false;
        if (wanted != "") return true;
        Say("Type the text to find first.", "caution");
        return false;
    }
}
