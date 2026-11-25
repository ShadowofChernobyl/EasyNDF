using System.Windows;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Editor;
using EasyNdf.App.Text;
using EasyNdf.Data.FileList;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace EasyNdf.App.Pages;

/// <summary>
/// The File Editor's keys beyond finding (<see cref="FindBarKeys"/>), as code editors have them: Ctrl+S saves and
/// Ctrl+O opens; Ctrl+PgDn and Ctrl+PgUp go through the open files and Ctrl+Shift+T reopens the one closed last;
/// Ctrl+E shows the export and Ctrl+Shift+E its changes only; Alt+Up and Alt+Down move the lines, with Shift copy
/// them; Ctrl+Shift+K deletes them and Ctrl+/ comments them; Ctrl+Enter and Ctrl+Shift+Enter start a line below or
/// above; F12 goes to the descriptor named at the caret, Alt+Left and Alt+Right back and forward through the places
/// left; Ctrl+Shift+\ jumps to the matching bracket; Ctrl+P and Ctrl+Shift+O put the keys in the file box and the
/// Descriptor Tree's filter; Ctrl+M folds the block at the caret; Alt+Z wraps long lines.
/// </summary>
public partial class EditorPage
{
    /// <summary>A place left by a jump: the file (its row on the Files tab), and where the caret was.</summary>
    private sealed record Place(FileEntry? Entry, int Offset);

    private readonly List<Place> placesBack = [], placesForward = [];

    /// <summary>The files closed, newest last, for Ctrl+Shift+T.</summary>
    private readonly List<FileEntry> closedFiles = [];

    /// <summary>
    /// A key pressed while the Editor is shown and the keys are elsewhere in its window: the page's keys work as they
    /// do inside it, so Ctrl+O opens a file from an Editor with none open.
    /// </summary>
    internal void HandleKeysFromOutside(KeyEventArgs e)
    {
        if (IsKeyboardFocusWithin) return;
        FindBarKeys(this, e);
        if (!e.Handled) ShortcutKeys(this, e);
    }

    /// <summary>The mouse's Back or Forward button: as Alt+Left or Alt+Right. False with no place to go to.</summary>
    internal bool GoBackOrForward(bool back)
    {
        if ((back ? placesBack.Count : placesForward.Count) == 0) return false;
        _ = back ? GoToPlace(placesBack, placesForward) : GoToPlace(placesForward, placesBack);
        return true;
    }

    private void ShortcutKeys(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        ModifierKeys keys = Keyboard.Modifiers;
        // a key with Alt comes as a system key
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool control = keys == ModifierKeys.Control, controlShift = keys == (ModifierKeys.Control | ModifierKeys.Shift);
        bool alt = keys == ModifierKeys.Alt, shiftAlt = keys == (ModifierKeys.Shift | ModifierKeys.Alt);
        bool hasFile = openViews.Contains(view) && view.Shown is not null;
        bool inText = hasFile && view.Editor.TextArea.IsKeyboardFocusWithin;
        bool canType = inText && !view.Editor.IsReadOnly;

        if (control && key == Key.S && SaveButton.IsEnabled) SaveClicked(this, new RoutedEventArgs());
        else if (control && key == Key.O) OpenFileClicked(this, new RoutedEventArgs());
        else if (control && key is Key.Next or Key.Prior && openViews.Count > 1) NextFile(key == Key.Next ? +1 : -1);
        else if (controlShift && key == Key.T) _ = ReopenClosedFile();
        else if (control && key == Key.E && ExportButton.IsEnabled) _ = ShowExportOrSource();
        else if (controlShift && key == Key.E && ChangesButton.IsEnabled) ChangesClicked(this, new RoutedEventArgs());
        else if (alt && key is Key.Up or Key.Down && canType) MoveLines(key == Key.Up ? -1 : +1);
        else if (shiftAlt && key is Key.Up or Key.Down && canType) CopyLines(key == Key.Up ? -1 : +1);
        else if (controlShift && key == Key.K && canType) DeleteLines();
        else if (control && key is Key.OemQuestion or Key.Divide && canType) CommentLines();
        else if (control && key == Key.Enter && canType) NewLine(below: true);
        else if (controlShift && key == Key.Enter && canType) NewLine(below: false);
        else if (keys == ModifierKeys.None && key == Key.F12 && inText) _ = GoToDescriptorAtCaret();
        else if (alt && key == Key.Left) _ = GoToPlace(placesBack, placesForward);
        else if (alt && key == Key.Right) _ = GoToPlace(placesForward, placesBack);
        else if (controlShift && key is Key.OemPipe or Key.Oem5 or Key.OemBackslash && inText) GoToMatchingBracket();
        else if (control && key == Key.P) { FilePick.Focus(); FilePick.IsDropDownOpen = true; }
        else if (controlShift && key == Key.O) { if (Panes.IsFolded) Panes.Fold(false); DescriptorFind.Focus(); DescriptorFind.SelectAll(); }
        else if (control && key == Key.M && inText) FoldBlockAtCaret();
        else if (alt && key == Key.Z) ToggleWordWrap();
        else return;
        e.Handled = true;
    }

    // ---- files ----

    private void NextFile(int direction)
    {
        int at = openViews.IndexOf(view);
        FileView next = openViews[((at < 0 ? 0 : at) + direction + openViews.Count) % openViews.Count];
        Bring(next);
        next.Editor.Focus();
    }

    /// <summary>Ctrl+Shift+T: the file closed last that is still on the Files tab opens again.</summary>
    private async Task ReopenClosedFile()
    {
        while (closedFiles.Count > 0)
        {
            FileEntry entry = closedFiles[^1];
            closedFiles.RemoveAt(closedFiles.Count - 1);
            if (entry.Source is null || Files?.Entries.Contains(entry) != true || TabOf(entry) is not null) continue;
            await Open(entry);
            return;
        }
    }

    // ---- lines ----

    /// <summary>The lines the selection touches, or the caret's line: a selection that ends at a line's start leaves that line out.</summary>
    private (DocumentLine First, DocumentLine Last) LinesInUse()
    {
        TextEditor editor = view.Editor;
        TextDocument document = editor.Document;
        DocumentLine first = document.GetLineByOffset(editor.SelectionStart);
        int end = editor.SelectionStart + editor.SelectionLength;
        DocumentLine last = document.GetLineByOffset(end);
        if (editor.SelectionLength > 0 && end == last.Offset && last.LineNumber > first.LineNumber) last = last.PreviousLine;
        return (first, last);
    }

    /// <summary>Alt+Up and Alt+Down: the lines change places with the line above or below; the selection goes with them.</summary>
    private void MoveLines(int direction)
    {
        TextEditor editor = view.Editor;
        TextDocument document = editor.Document;
        var (first, last) = LinesInUse();
        DocumentLine? other = direction < 0 ? first.PreviousLine : last.NextLine;
        if (other is null) return;
        int start = editor.SelectionStart, length = editor.SelectionLength, caret = editor.CaretOffset;
        string block = document.GetText(first.Offset, last.EndOffset - first.Offset), otherText = document.GetText(other);
        int from = direction < 0 ? other.Offset : first.Offset, until = direction < 0 ? last.EndOffset : other.EndOffset;
        document.Replace(from, until - from, direction < 0 ? block + "\n" + otherText : otherText + "\n" + block);
        int shift = direction * (otherText.Length + 1);
        editor.Select(start + shift, length);
        editor.CaretOffset = caret + shift;
        editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>Shift+Alt+Up and Shift+Alt+Down: a copy of the lines above or below them; the caret stays in the copy that way.</summary>
    private void CopyLines(int direction)
    {
        TextEditor editor = view.Editor;
        TextDocument document = editor.Document;
        var (first, last) = LinesInUse();
        int start = editor.SelectionStart, length = editor.SelectionLength, caret = editor.CaretOffset;
        string block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        document.Insert(last.EndOffset, "\n" + block);
        int shift = direction > 0 ? block.Length + 1 : 0;
        editor.Select(start + shift, length);
        editor.CaretOffset = caret + shift;
        editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>Ctrl+Shift+K: the lines go, with their line break.</summary>
    private void DeleteLines()
    {
        TextDocument document = view.Editor.Document;
        var (first, last) = LinesInUse();
        int from = first.Offset, until = last.EndOffset + last.DelimiterLength;
        // the last lines of the text take the line break before them
        if (last.NextLine is null && first.PreviousLine is DocumentLine before) from = before.EndOffset;
        document.Remove(from, until - from);
    }

    /// <summary>Ctrl+/: "// " before each line's text, or taken away when every line with text has it.</summary>
    private void CommentLines()
    {
        TextDocument document = view.Editor.Document;
        var (first, last) = LinesInUse();
        var lines = new List<(DocumentLine Line, int TextAt)>();
        for (DocumentLine? line = first; line is not null && line.LineNumber <= last.LineNumber; line = line.NextLine)
        {
            string text = document.GetText(line);
            int at = text.Length - text.TrimStart().Length;
            if (at < text.Length) lines.Add((line, at));
        }
        if (lines.Count == 0) return;
        bool allCommented = lines.All(each => document.GetText(each.Line.Offset + each.TextAt, Math.Min(2, each.Line.Length - each.TextAt)) == "//");
        using (document.RunUpdate())
        {
            foreach (var (line, at) in Enumerable.Reverse(lines))
            {
                if (!allCommented) { document.Insert(line.Offset + at, "// "); continue; }
                int length = line.Length - at >= 3 && document.GetCharAt(line.Offset + at + 2) == ' ' ? 3 : 2;
                document.Remove(line.Offset + at, length);
            }
        }
    }

    /// <summary>Ctrl+Enter and Ctrl+Shift+Enter: a new line below or above the caret's, indented as it is, with the caret on it.</summary>
    private void NewLine(bool below)
    {
        TextEditor editor = view.Editor;
        TextDocument document = editor.Document;
        DocumentLine line = document.GetLineByOffset(editor.CaretOffset);
        string text = document.GetText(line), indent = text[..(text.Length - text.TrimStart().Length)];
        if (below)
        {
            document.Insert(line.EndOffset, "\n" + indent);
            editor.CaretOffset = line.EndOffset + 1 + indent.Length;
        }
        else
        {
            int at = line.Offset;
            document.Insert(at, indent + "\n");
            editor.CaretOffset = at + indent.Length;
        }
        editor.TextArea.Caret.BringCaretToView();
    }

    // ---- moving around ----

    /// <summary>Keeps where the caret is before a jump, for Alt+Left to come back to.</summary>
    private void RememberPlace()
    {
        if (view.Shown is null) return;
        placesBack.Add(new Place(view.Shown.Entry, view.Editor.CaretOffset));
        if (placesBack.Count > 100) placesBack.RemoveAt(0);
        placesForward.Clear();
    }

    /// <summary>Alt+Left and Alt+Right: back to the place left by the last jump, or forward again.</summary>
    private async Task GoToPlace(List<Place> from, List<Place> to)
    {
        while (from.Count > 0)
        {
            Place place = from[^1];
            from.RemoveAt(from.Count - 1);
            if (place.Entry is FileEntry entry && (entry.Source is null || Files?.Entries.Contains(entry) != true)) continue;
            if (view.Shown is not null) to.Add(new Place(view.Shown.Entry, view.Editor.CaretOffset));
            if (place.Entry is FileEntry other && view.Shown?.Entry != other) await Open(other);
            if (view.Shown is null || (place.Entry is not null && view.Shown.Entry != place.Entry)) return;
            TextEditor editor = view.Editor;
            editor.CaretOffset = Math.Clamp(place.Offset, 0, editor.Document.TextLength);
            editor.ScrollToLine(editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
            editor.Focus();
            return;
        }
    }

    /// <summary>The name at the caret: letters, digits and underscores, as a descriptor is named.</summary>
    private string NameAtCaret()
    {
        TextDocument document = view.Editor.Document;
        int at = Math.Clamp(view.Editor.CaretOffset, 0, document.TextLength), start = at, end = at;
        static bool InName(char c) => char.IsLetterOrDigit(c) || c == '_';
        while (start > 0 && InName(document.GetCharAt(start - 1))) start--;
        while (end < document.TextLength && InName(document.GetCharAt(end))) end++;
        return document.GetText(start, end - start);
    }

    /// <summary>
    /// F12: the descriptor named at the caret is shown, in this file or in the file of the Files tab that has it; the
    /// place left can be come back to with Alt+Left.
    /// </summary>
    private async Task GoToDescriptorAtCaret()
    {
        string name = NameAtCaret();
        if (name == "") return;
        int here = shown is { IsFile: true } ? outline.FindIndex(descriptor => descriptor.Name == name) : -1;
        if (here >= 0)
        {
            RememberPlace();
            otherDescriptor = null;
            await SelectDescriptor(here, reveal: true, keepClosed: false);
            ShowTree();
            return;
        }
        if (ReferenceIndex.Of([.. Files?.Entries ?? []]).Names.TryGetValue(name, out ReferenceIndex.Declared declared))
        {
            RememberPlace();
            await ShowDescriptor(declared.Entry, name);
            return;
        }
        Say(Texts.Get("Editor.GoTo.NotADescriptor", ("name", name)) + UnreadNote(), NoticeKind.Caution);
    }

    /// <summary>
    /// Ctrl+Shift+\: from a bracket at the caret to the one that closes or opens it; from inside a pair, to its opening
    /// bracket. Brackets inside quotes are passed over.
    /// </summary>
    private void GoToMatchingBracket()
    {
        TextEditor editor = view.Editor;
        TextDocument document = editor.Document;
        const string open = "([{", close = ")]}";
        int caret = editor.CaretOffset, length = document.TextLength;
        int at = caret < length && (open + close).Contains(document.GetCharAt(caret)) ? caret
            : caret > 0 && (open + close).Contains(document.GetCharAt(caret - 1)) ? caret - 1 : -1;
        int found = -1;
        if (at >= 0 && open.Contains(document.GetCharAt(at))) found = Scan(at + 1, +1, 1);
        else if (at >= 0) found = Scan(at - 1, -1, 1);
        else found = Scan(caret - 1, -1, 1);
        if (found < 0) return;
        RememberPlace();
        editor.CaretOffset = found;
        editor.TextArea.Caret.BringCaretToView();

        // walks until the depth is back to none: the bracket that pairs with where it began
        int Scan(int from, int step, int depth)
        {
            char quote = '\0';
            for (int i = from, seen = 0; i >= 0 && i < length && seen < 4_000_000; i += step, seen++)
            {
                char c = document.GetCharAt(i);
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '"' or '\'') { quote = c; continue; }
                bool opens = open.Contains(c), closes = close.Contains(c);
                if (!opens && !closes) continue;
                depth += (step > 0) == opens ? 1 : -1;
                if (depth == 0) return i;
            }
            return -1;
        }
    }

    // ---- folding and view ----

    /// <summary>Ctrl+M: the block the caret is in folds, or unfolds: the nearest line above that starts a block reaching the caret's line.</summary>
    private void FoldBlockAtCaret()
    {
        if (shown is null) return;
        int caretLine = view.Editor.TextArea.Caret.Line - 1;
        for (int line = caretLine, looked = 0; line >= 0 && looked < 20_000; line--, looked++)
        {
            int end = BlockEndOf(view, line);
            if (end <= line || end < caretLine) continue;
            ToggleBlock(line);
            return;
        }
    }

    /// <summary>Alt+Z: the "Wrap long lines" setting, switched.</summary>
    private void ToggleWordWrap()
    {
        bool wraps = AppData.Current.Settings["wordWrap"]?.GetValue<bool>() == true;
        AppData.Current.Settings["wordWrap"] = !wraps;
        AppData.Current.SaveSettings();
        MainView.Of(this).SettingsChanged("wordWrap");
    }
}
