using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace EasyNdf.App.Editor;

/// <summary>
/// Closing pairs as Sublime Text types them, while the "Close brackets and quotes" setting is on: typing ( [ { " or '
/// also types its partner and puts the cursor between them; typing the partner when it is already next steps over it;
/// Backspace between an empty pair takes both away; and with text selected, the pair goes around it.
/// </summary>
public sealed class AutoPairs
{
    /// <summary>Each opening character and the one that closes it.</summary>
    private static readonly Dictionary<char, char> Partners = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"',
        ['\''] = '\'',
    };

    private readonly TextArea area;

    /// <summary>Watches what is typed in a text area.</summary>
    public AutoPairs(TextArea area)
    {
        this.area = area;
        area.TextEntering += TextEntering;
        area.PreviewKeyDown += KeyPressed;
    }

    /// <summary>Whether the "Close brackets and quotes" setting is on.</summary>
    public static bool IsOn => AppData.Current.Settings["autoPairs"]?.GetValue<bool>() != false;

    private TextDocument Document => area.Document;

    private void TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (!IsOn || e.Text.Length != 1 || !area.ReadOnlySectionProvider.CanInsert(area.Caret.Offset)) return;
        char typed = e.Text[0];
        int caret = area.Caret.Offset;
        char before = caret > 0 ? Document.GetCharAt(caret - 1) : '\0';
        char after = caret < Document.TextLength ? Document.GetCharAt(caret) : '\0';

        if (area.Selection.IsEmpty && after == typed && Partners.ContainsValue(typed))
        {
            // the partner is already there: step over it rather than type a second one
            area.Caret.Offset = caret + 1;
            e.Handled = true;
            return;
        }
        if (!Partners.TryGetValue(typed, out char closing)) return;
        if (!area.Selection.IsEmpty)
        {
            WrapSelection(typed, closing);
            e.Handled = true;
            return;
        }
        // Next to a word, a quote is an apostrophe or the end of a text, and a bracket is meant to open on that word.
        bool isQuote = typed is '"' or '\'';
        if (IsWordCharacter(after) || (isQuote && IsWordCharacter(before))) return;
        Document.Insert(caret, typed.ToString() + closing);
        area.Caret.Offset = caret + 1;
        e.Handled = true;
    }

    /// <summary>Puts the pair around the selected text, which stays selected.</summary>
    private void WrapSelection(char opening, char closing)
    {
        int start = area.Selection.SurroundingSegment.Offset, length = area.Selection.SurroundingSegment.Length;
        string selected = Document.GetText(start, length);
        Document.Replace(start, length, opening + selected + closing);
        area.Selection = Selection.Create(area, start + 1, start + 1 + length);
        area.Caret.Offset = start + 1 + length;
    }

    /// <summary>Backspace between an empty pair, such as (), takes both away.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (!IsOn || e.Key != Key.Back || Keyboard.Modifiers != ModifierKeys.None || !area.Selection.IsEmpty) return;
        int caret = area.Caret.Offset;
        if (caret == 0 || caret >= Document.TextLength) return;
        char before = Document.GetCharAt(caret - 1), after = Document.GetCharAt(caret);
        if (!Partners.TryGetValue(before, out char closing) || closing != after) return;
        if (!area.ReadOnlySectionProvider.CanInsert(caret)) return;
        Document.Remove(caret - 1, 2);
        e.Handled = true;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';
}
