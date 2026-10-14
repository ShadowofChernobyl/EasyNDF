using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyNdf.App.Controls;

/// <summary>One suggestion: what is shown, in which theme colour, a dim note after it, its tooltip, and what goes into the box.</summary>
/// <param name="Text">The word shown, for example "LastIndex".</param>
/// <param name="Colour">The theme colour it is shown in, for example "s-keyword".</param>
/// <param name="Note">A few dim words saying what it is, for example "last item of a list".</param>
/// <param name="Insert">What goes into the box when it is picked; null for <paramref name="Text"/>.</param>
/// <param name="Tip">The tooltip saying what it is, in the Help wiki's words where it has them; empty for none.</param>
public sealed record Suggestion(string Text, string Colour, string Note = "", string? Insert = null, string Tip = "")
{
    /// <summary>What goes into the box when it is picked.</summary>
    public string Inserted => Insert ?? Text;
}

/// <summary>What to suggest at the caret: where the word being typed starts, and the words that could go there.</summary>
/// <param name="WordStart">Where the word being typed starts; it is replaced by the word picked.</param>
/// <param name="Choices">Every word that fits there, before they are narrowed to what has been typed.</param>
public sealed record SuggestionsAt(int WordStart, IReadOnlyList<Suggestion> Choices);

/// <summary>
/// Suggestions while typing, as VS Code's IntelliSense gives them: a list under the word being typed, of the words that
/// fit there and start with (or hold) what has been typed so far. Up and Down move in it, Enter or Tab puts the word in,
/// Escape closes it, and Ctrl+Space opens it without typing. A click on a word puts it in too. What fits where is up to
/// the box (see <see cref="Attach"/>).
/// </summary>
public static class Suggestions
{
    /// <summary>The most words shown at once; the list scrolls for more.</summary>
    private const int MostShown = 8;

    private static readonly ConditionalWeakTable<TextBox, Shown> shown = [];

    /// <summary>Gives a text box suggestions; <paramref name="source"/> says what fits at a place in a text.</summary>
    public static void Attach(TextBox box, Func<string, int, SuggestionsAt?> source)
    {
        var list = new Shown(box, source);
        shown.AddOrUpdate(box, list);
    }

    /// <summary>Whether a box's list is open, so the window it is in leaves Enter and Escape to it.</summary>
    public static bool IsOpenFor(TextBox box) => shown.TryGetValue(box, out Shown? list) && list.IsOpen;

    /// <summary>
    /// Fills a box's list for the word at its caret and gives back the list's box, where it would open (from the text
    /// box's top left) and its words; the list itself is left closed.
    /// </summary>
    internal static (FrameworkElement List, Point At, List<string> Words)? ShowAtCaret(TextBox box)
    {
        if (!shown.TryGetValue(box, out Shown? list)) return null;
        return list.ShowAtCaret();
    }

    /// <summary>One box's list and the keys and typing that open, move and close it.</summary>
    private sealed class Shown
    {
        private readonly TextBox box;
        private readonly Func<string, int, SuggestionsAt?> source;
        private readonly Popup popup = new() { AllowsTransparency = true, Placement = PlacementMode.Relative, StaysOpen = true, Focusable = false };
        private readonly ListBox words = new() { Focusable = false, MaxHeight = MostShown * 20 + 2, MinWidth = 180 };
        private int wordStart;
        private bool picking;

        public Shown(TextBox box, Func<string, int, SuggestionsAt?> source)
        {
            this.box = box;
            this.source = source;
            words.SetResourceReference(FrameworkElement.StyleProperty, "SuggestionList");
            var edge = new Bevel { Child = words };
            edge.SetResourceReference(Bevel.TopLeftProperty, "hi");
            edge.SetResourceReference(Bevel.BottomRightProperty, "lo");
            edge.SetResourceReference(Bevel.BackgroundProperty, "panel");
            popup.Child = edge;
            popup.PlacementTarget = box;
            words.PreviewMouseLeftButtonUp += WordClicked;

            box.TextChanged += (_, _) => Typed();
            box.PreviewKeyDown += KeyPressed;
            box.LostKeyboardFocus += (_, _) => Close();
            box.SelectionChanged += (_, _) => { if (IsOpen && (box.CaretIndex < wordStart || box.SelectionLength > 0)) Close(); };
            box.Unloaded += (_, _) => Close();
        }

        public bool IsOpen => popup.IsOpen;

        public (FrameworkElement List, Point At, List<string> Words)? ShowAtCaret()
        {
            Show(evenEmpty: true);
            if (!popup.IsOpen) return null;
            popup.IsOpen = false;
            List<string> texts = [.. words.Items.OfType<FrameworkElement>().Select(row => ((Suggestion)row.Tag).Text)];
            return ((FrameworkElement)popup.Child, new Point(popup.HorizontalOffset, popup.VerticalOffset), texts);
        }

        /// <summary>Text typed: the list opens or narrows while a word is being typed, and closes when nothing fits.</summary>
        private void Typed()
        {
            // only typing opens it, not a text put in the box from elsewhere (undo, the window drawn again) or a word picked
            if (!box.IsKeyboardFocused || picking) return;
            Show(evenEmpty: false);
        }

        /// <summary>Fills the list for the word at the caret; with <paramref name="evenEmpty"/>, also before anything is typed.</summary>
        private void Show(bool evenEmpty)
        {
            int caret = box.CaretIndex;
            if (source(box.Text, caret) is not { } at || at.WordStart > caret) { Close(); return; }
            string typed = box.Text[at.WordStart..caret];
            if (typed == "" && !evenEmpty) { Close(); return; }
            List<Suggestion> fitting = [.. Narrowed(at.Choices, typed)];
            if (fitting.Count == 0 || fitting.Count == 1 && fitting[0].Inserted == typed) { Close(); return; }
            wordStart = at.WordStart;
            words.ItemsSource = fitting.Select(Row).ToList();
            words.SelectedIndex = 0;
            words.ScrollIntoView(words.SelectedItem);
            Rect place = box.GetRectFromCharacterIndex(wordStart);
            popup.HorizontalOffset = place.IsEmpty ? 0 : place.Left - 4;
            popup.VerticalOffset = place.IsEmpty ? box.ActualHeight : place.Bottom + 3;
            popup.IsOpen = true;
        }

        /// <summary>A word's line in the list: the word in the code font and its colour, and its note dim at the right.</summary>
        private static FrameworkElement Row(Suggestion word)
        {
            var line = new DockPanel { Tag = word, Background = Brushes.Transparent };
            // the whole line shows the tooltip, the space between the word and its note too
            if (word.Tip != "") line.ToolTip = word.Tip;
            var note = new TextBlock { Text = word.Note, Margin = new Thickness(16, 0, 0, 0), FontSize = 12 };
            note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            DockPanel.SetDock(note, Dock.Right);
            var text = new TextBlock { Text = word.Text, FontSize = 12 };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            text.SetResourceReference(TextBlock.ForegroundProperty, word.Colour);
            line.Children.Add(note);
            line.Children.Add(text);
            return line;
        }

        /// <summary>The words that start with what was typed, then those that only hold it, ignoring capitals.</summary>
        private static IEnumerable<Suggestion> Narrowed(IReadOnlyList<Suggestion> choices, string typed)
        {
            if (typed == "") return choices;
            var starting = choices.Where(c => c.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            var holding = choices.Where(c => !c.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && c.Text.Contains(typed, StringComparison.OrdinalIgnoreCase));
            return starting.Concat(holding);
        }

        private void KeyPressed(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Show(evenEmpty: true);
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
                default: return;
            }
            e.Handled = true;
        }

        private void Move(int by)
        {
            int count = words.Items.Count;
            words.SelectedIndex = Math.Clamp(words.SelectedIndex + by, 0, count - 1);
            words.ScrollIntoView(words.SelectedItem);
        }

        private void WordClicked(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(words, e.OriginalSource as DependencyObject) is not ListBoxItem item) return;
            words.SelectedItem = item.Content;
            Pick();
            e.Handled = true;
        }

        /// <summary>Puts the chosen word in place of the word being typed, with the caret after it.</summary>
        private void Pick()
        {
            if (words.SelectedItem is not FrameworkElement { Tag: Suggestion chosen }) { Close(); return; }
            int caret = box.CaretIndex;
            Close();
            string text = box.Text;
            // a word picked replaces the whole word the caret is in, not only the part before the caret
            int wordEnd = caret;
            while (wordEnd < text.Length && (char.IsAsciiLetterOrDigit(text[wordEnd]) || text[wordEnd] == '_')) wordEnd++;
            picking = true;
            box.Text = text[..wordStart] + chosen.Inserted + text[wordEnd..];
            box.CaretIndex = wordStart + chosen.Inserted.Length;
            picking = false;
            box.Focus();
        }

        private void Close() => popup.IsOpen = false;
    }
}
