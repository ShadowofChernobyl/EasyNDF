using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EasyNdf.App.Controls;
using EasyNdf.Engine.Editing;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace EasyNdf.App.Editor;

/// <summary>
/// The File Editor's suggestions while typing, as the mock chose, with the rule boxes' look and keys: a list under the
/// word being typed and, beside it, the tip of the one chosen. Only what the check accepts is offered. Where a name goes,
/// the names the object's type takes that the object doesn't have yet; after "Name =", the values that name can take
/// (True and False, the members of its choice list, or the entries the files give it), unless the object already has
/// the name, as no value can make that line right. Up and Down move, Enter or Tab puts the word in, Escape closes it,
/// and Ctrl+Space opens it without typing.
/// </summary>
public sealed class EditorSuggestions
{
    /// <summary>The most words shown at once; the list scrolls for more.</summary>
    private const int MostShown = 8;

    /// <summary>What opened or changed the list, which decides whether it opens before anything is typed.</summary>
    private enum Reason
    {
        /// <summary>A letter or digit typed: opens on the word typed so far.</summary>
        Typed,

        /// <summary>"=" or a space typed: a value's list opens even with nothing typed yet.</summary>
        Separator,

        /// <summary>Ctrl+Space, or a name just put in: opens even with nothing typed.</summary>
        Asked,
    }

    /// <summary>One word of the list: a name (its value type dim beside it) or a value.</summary>
    private sealed record Item(string Text, bool IsValue, string Note, string Mark);

    private readonly TextEditor editor;
    private readonly Popup popup = new() { AllowsTransparency = true, Placement = PlacementMode.Relative, StaysOpen = true, Focusable = false };
    private readonly Popup tipPopup = new() { AllowsTransparency = true, Placement = PlacementMode.Right, StaysOpen = true, Focusable = false, HorizontalOffset = 4 };
    private readonly ListBox words = new() { Focusable = false, MaxHeight = MostShown * 20 + 2, MinWidth = 260 };
    private readonly Bevel tipBox = new() { Padding = new Thickness(9, 6, 9, 7) };

    /// <summary>Where the word being typed starts; the word picked takes its place.</summary>
    private int wordStart;

    /// <summary>The type of the object the list is for, and the name whose values it lists ("" for names).</summary>
    private string objectType = "", valuesOf = "";

    /// <summary>True while a word picked is put in, which must not open the list again by itself.</summary>
    private bool picking;

    /// <summary>The guide for the text shown; null gives no suggestions (Steel Division 2's files, an export).</summary>
    public Func<ValueGuide?> Guide { get; set; } = () => null;

    /// <summary>Gives the editor suggestions.</summary>
    public EditorSuggestions(TextEditor editor)
    {
        this.editor = editor;
        words.SetResourceReference(FrameworkElement.StyleProperty, "SuggestionList");
        var edge = new Bevel { Child = words };
        edge.SetResourceReference(Bevel.TopLeftProperty, "hi");
        edge.SetResourceReference(Bevel.BottomRightProperty, "lo");
        edge.SetResourceReference(Bevel.BackgroundProperty, "panel");
        popup.Child = edge;
        popup.PlacementTarget = editor.TextArea.TextView;
        tipBox.SetResourceReference(Bevel.TopLeftProperty, "hi");
        tipBox.SetResourceReference(Bevel.BottomRightProperty, "lo");
        tipBox.SetResourceReference(Bevel.BackgroundProperty, "panel");
        // written as the tooltips are, not in the text's code font, which the list would hand down to it
        tipBox.SetResourceReference(TextElement.FontFamilyProperty, "ui-font");
        tipBox.SetResourceReference(TextElement.ForegroundProperty, "ink");
        TextElement.SetFontSize(tipBox, 12);
        TextBlock.SetLineHeight(tipBox, 17);
        TextBlock.SetLineStackingStrategy(tipBox, LineStackingStrategy.BlockLineHeight);
        tipPopup.Child = tipBox;
        tipPopup.PlacementTarget = edge;
        // picked on the press, which is then handled: let through, it took the keyboard from the text, which closed the
        // list before the release could pick anything
        words.PreviewMouseLeftButtonDown += WordClicked;
        words.SelectionChanged += (_, _) => ShowChosenTip();

        editor.TextArea.TextEntered += TextEntered;
        editor.TextArea.PreviewKeyDown += KeyPressed;
        editor.TextArea.LostKeyboardFocus += (_, _) => Close();
        editor.TextArea.Caret.PositionChanged += (_, _) => CloseWhenCaretLeft();
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => FollowScroll();
        editor.DocumentChanged += (_, _) => Close();
        editor.Unloaded += (_, _) => Close();
    }

    /// <summary>Whether the list is open.</summary>
    public bool IsOpen => popup.IsOpen;

    /// <summary>The words listed, as shown; for the checks.</summary>
    internal IReadOnlyList<string> Shown => [.. words.Items.OfType<FrameworkElement>().Select(row => ((Item)row.Tag).Text)];

    /// <summary>Closes the list and its tip.</summary>
    public void Close()
    {
        popup.IsOpen = false;
        tipPopup.IsOpen = false;
    }

    /// <summary>Opens the list at the caret as Ctrl+Space does; for the checks.</summary>
    internal void OpenAtCaret() => Show(Reason.Asked);

    // ---- what fits where ----

    /// <summary>Fills the list for the word at the caret and opens it, or closes it when nothing fits.</summary>
    private void Show(Reason reason)
    {
        if (Guide() is not { } guide || editor.IsReadOnly || editor.Document is not { } document) { Close(); return; }
        int caret = editor.CaretOffset;
        if (EditorPlaces.At(document, caret) is not { } place || !guide.Knows(place.ObjectType) || place.WordStart > caret) { Close(); return; }
        string typed = document.GetText(place.WordStart, caret - place.WordStart);
        List<Item>? items = place.IsInValue ? ValuesFor(place, guide, document, caret) : NamesFor(place, guide, document);
        bool opensEmpty = reason == Reason.Asked || (place.IsInValue && reason == Reason.Separator);
        if (items is null || (typed == "" && !opensEmpty)) { Close(); return; }
        List<Item> fitting = [.. Narrowed(items, typed)];
        if (fitting.Count == 0 || (fitting.Count == 1 && fitting[0].Text == typed)) { Close(); return; }
        objectType = place.ObjectType;
        valuesOf = place.IsInValue ? place.Name : "";
        wordStart = place.WordStart;
        words.ItemsSource = fitting.Select(item => Row(item, typed)).ToList();
        words.SelectedIndex = 0;
        words.ScrollIntoView(words.SelectedItem);
        PlaceUnderWord(document);
        popup.IsOpen = true;
        ShowChosenTip();
    }

    /// <summary>The names the object's type takes that it doesn't have yet; null where no name goes (in the middle of a line).</summary>
    private static List<Item>? NamesFor(NdfPlace place, ValueGuide guide, TextDocument document)
    {
        if (!IsWhereANameGoes(document, place.WordStart)) return null;
        return [.. guide.NamesOf(place.ObjectType)
            .Where(name => !place.NamesSet.ContainsKey(name))
            .Select(name => guide.Find(place.ObjectType, name) is { } info ? new Item(name, false, info.ValueType, CertaintyMark(info.Certainty)) : null)
            .OfType<Item>()];
    }

    /// <summary>
    /// The values a name can take that the check accepts; null when it has no list, or the object already has the name
    /// on an earlier line (no value can make this line right).
    /// </summary>
    private static List<Item>? ValuesFor(NdfPlace place, ValueGuide guide, TextDocument document, int caret)
    {
        if (guide.Find(place.ObjectType, place.Name) is not { Choices.Count: > 0 } info) return null;
        int lineStart = document.GetLineByOffset(caret).Offset;
        if (place.NamesSet.TryGetValue(place.Name, out int firstValue) && firstValue < lineStart) return null;
        return [.. info.Choices
            .Where(choice => ValueChecker.ProblemWith(place.ObjectType, place.Name, choice, guide) is null)
            .Select(choice => new Item(choice, true, "", ""))];
    }

    /// <summary>Whether a name can start at a place: first on its line, or just after an object's "(".</summary>
    private static bool IsWhereANameGoes(TextDocument document, int place)
    {
        DocumentLine line = document.GetLineByOffset(place);
        string before = document.GetText(line.Offset, place - line.Offset).TrimEnd();
        return before.Length == 0 || before.EndsWith('(');
    }

    /// <summary>≈ for a name that is very likely but not confirmed, ? for one not known, as the Armory marks them.</summary>
    private static string CertaintyMark(string certainty) => certainty switch
    {
        "approx" or "plain" => "≈",
        "unclear" => "?",
        _ => "",
    };

    /// <summary>The words that start with what was typed, then those that only hold it, ignoring capitals.</summary>
    private static IEnumerable<Item> Narrowed(List<Item> items, string typed)
    {
        if (typed == "") return items;
        var starting = items.Where(item => item.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
        var holding = items.Where(item => !item.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && item.Text.Contains(typed, StringComparison.OrdinalIgnoreCase));
        return starting.Concat(holding);
    }

    // ---- showing it ----

    /// <summary>
    /// A word's line in the list: the word in the code font, a name in the types' colour and a value in the
    /// references', the part typed so far in bold, its mark (≈ or ?) small, and a name's value type dim at the right.
    /// </summary>
    private static FrameworkElement Row(Item item, string typed)
    {
        var line = new DockPanel { Tag = item, Background = Brushes.Transparent };
        var note = new TextBlock { Text = item.Note, Margin = new Thickness(16, 0, 0, 0), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(note, Dock.Right);
        var text = new TextBlock { FontSize = 12 };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        text.SetResourceReference(TextBlock.ForegroundProperty, item.IsValue ? "s-ref" : "s-type");
        int at = typed == "" ? -1 : item.Text.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
        if (at < 0) text.Inlines.Add(new Run(item.Text));
        else
        {
            text.Inlines.Add(new Run(item.Text[..at]));
            var match = new Run(item.Text.Substring(at, typed.Length)) { FontWeight = FontWeights.Bold };
            match.SetResourceReference(TextElement.ForegroundProperty, "accent");
            text.Inlines.Add(match);
            text.Inlines.Add(new Run(item.Text[(at + typed.Length)..]));
        }
        if (item.Mark != "")
        {
            var mark = new Run(item.Mark) { FontSize = 10, BaselineAlignment = BaselineAlignment.Superscript };
            mark.SetResourceReference(TextElement.ForegroundProperty, "muted");
            text.Inlines.Add(mark);
        }
        line.Children.Add(note);
        line.Children.Add(text);
        return line;
    }

    /// <summary>Puts the list just under the word being typed.</summary>
    private void PlaceUnderWord(TextDocument document)
    {
        TextView view = editor.TextArea.TextView;
        var place = new TextViewPosition(document.GetLocation(wordStart));
        Point under = view.GetVisualPosition(place, VisualYPosition.LineBottom) - view.ScrollOffset;
        popup.HorizontalOffset = under.X - 4;
        popup.VerticalOffset = under.Y + 2;
    }

    /// <summary>
    /// The text scrolled (typing near the bottom scrolls it to keep the caret in sight): the list follows the word, and
    /// closes once the word is out of sight.
    /// </summary>
    private void FollowScroll()
    {
        if (!IsOpen || editor.Document is not { } document) return;
        PlaceUnderWord(document);
        TextView view = editor.TextArea.TextView;
        if (popup.VerticalOffset < 0 || popup.VerticalOffset > view.ActualHeight) { Close(); return; }
        // the tip beside the list is placed again with it
        tipPopup.IsOpen = false;
        ShowChosenTip();
    }

    /// <summary>The chosen word's tip beside the list: the name's own tip, for a name or for one of its values.</summary>
    private void ShowChosenTip()
    {
        if (!popup.IsOpen || words.SelectedItem is not FrameworkElement { Tag: Item chosen } || Guide() is not { } guide) { tipPopup.IsOpen = false; return; }
        string name = chosen.IsValue ? valuesOf : chosen.Text;
        if (ValueTips.For(null, name, guide.Find(objectType, name), null) is not { } tip) { tipPopup.IsOpen = false; return; }
        tipBox.Child = tip;
        tipPopup.IsOpen = true;
    }

    // ---- typing and keys ----

    /// <summary>Text typed: a word's letters open or narrow the list; "=" or a space after a name opens its values.</summary>
    private void TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (picking || e.Text.Length == 0) return;
        char typed = e.Text[^1];
        if (char.IsAsciiLetterOrDigit(typed) || typed is '_' or '/' or '~' or '$' or '.') Show(Reason.Typed);
        else if (typed is '=' or ' ') Show(Reason.Separator);
        else Close();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Show(Reason.Asked);
            return;
        }
        if (!IsOpen) return;
        switch (e.Key)
        {
            case Key.Down: Move(+1); break;
            case Key.Up: Move(-1); break;
            case Key.PageDown: Move(+MostShown); break;
            case Key.PageUp: Move(-MostShown); break;
            case Key.Enter or Key.Tab when Keyboard.Modifiers == ModifierKeys.None: Pick(); break;
            case Key.Escape: Close(); break;
            case Key.Back or Key.Delete:
                // the list narrows again once the character is gone
                editor.Dispatcher.BeginInvoke(() => Show(Reason.Typed), DispatcherPriority.Input);
                return;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int by)
    {
        words.SelectedIndex = Math.Clamp(words.SelectedIndex + by, 0, words.Items.Count - 1);
        words.ScrollIntoView(words.SelectedItem);
    }

    /// <summary>A click on a word puts it in as Tab does, and the text keeps the keyboard.</summary>
    private void WordClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ItemsControl.ContainerFromElement(words, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
        words.SelectedItem = item.Content;
        Pick();
        editor.TextArea.Focus();
    }

    /// <summary>
    /// Puts the chosen word in place of the word being typed: a value as it is, a name with " = " after it, and then
    /// the name's values open.
    /// </summary>
    private void Pick()
    {
        if (words.SelectedItem is not FrameworkElement { Tag: Item chosen } || editor.Document is not { } document) { Close(); return; }
        int wordEnd = editor.CaretOffset;
        while (wordEnd < document.TextLength && IsWordCharacter(document.GetCharAt(wordEnd))) wordEnd++;
        string inserted = chosen.IsValue || NextIsEquals(document, wordEnd) ? chosen.Text : chosen.Text + " = ";
        Close();
        picking = true;
        try
        {
            document.Replace(wordStart, wordEnd - wordStart, inserted);
            editor.CaretOffset = wordStart + inserted.Length;
        }
        finally { picking = false; }
        if (!chosen.IsValue) Show(Reason.Asked);
    }

    /// <summary>Whether "=" already follows a place on its line, so a name picked there needs none.</summary>
    private static bool NextIsEquals(TextDocument document, int place)
    {
        while (place < document.TextLength && document.GetCharAt(place) is ' ' or '\t') place++;
        return place < document.TextLength && document.GetCharAt(place) == '=';
    }

    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '/' or '~' or '$' or '.';

    /// <summary>The caret left the word, or its line: the list closes.</summary>
    private void CloseWhenCaretLeft()
    {
        if (!IsOpen || picking || editor.Document is not { } document) return;
        int caret = editor.CaretOffset;
        if (caret < wordStart || document.GetLineByOffset(caret).LineNumber != document.GetLineByOffset(Math.Min(wordStart, document.TextLength)).LineNumber) Close();
    }
}
