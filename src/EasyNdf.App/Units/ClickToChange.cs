using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;

namespace EasyNdf.App.Units;

/// <summary>
/// A value of the Unit pane that reads as text and changes with a click: the click turns its cell into a box (or a
/// list to choose from) holding the value. As in the Division pane, what is typed is kept on Enter or when the box is
/// left, and Escape puts the value back; then the cell reads as text again. A value the box does not take outlines it
/// in the caution colour, and is put back. The value is saved once the box is gone: saving draws the pane again, which
/// must not happen while the box is still handling its key or its focus.
/// </summary>
public static class ClickToChange
{
    /// <summary>
    /// Makes a cell's value changeable in a box: a click on the cell shows the box, with the value selected.
    /// </summary>
    /// <param name="cell">The cell, whose child is the value's text block.</param>
    /// <param name="value">The value as it is typed, such as "2473" (no thousands separator, no unit).</param>
    /// <param name="isTaken">Whether a text typed is a value the box takes, for the caution outline.</param>
    /// <param name="keep">Saves a value typed, once the box is gone; false when the change was refused, which leaves the value as it was.</param>
    /// <param name="boxTip">The box's tooltip: what it takes.</param>
    public static void WithBox(Bevel cell, string value, Func<string, bool> isTaken, Func<string, bool> keep, string boxTip)
    {
        MakeClickable(cell, _ =>
        {
            string? typedToKeep = null;
            var box = new EditBox(value, double.NaN, typed =>
            {
                if (!isTaken(typed)) return false;
                typedToKeep = typed;
                return true;
            }) { ToolTip = boxTip, MinWidth = 40 };
            box.Unloaded += (_, _) =>
            {
                if (typedToKeep is not string typed) return;
                typedToKeep = null;
                box.Dispatcher.BeginInvoke(() => keep(typed));
            };
            box.TextChanged += (_, _) => ShowWhetherTaken(box, isTaken);
            box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
            {
                // the box keeps the value, or puts it back, on these keys first; then the cell reads as text again
                if (e.Key is Key.Enter or Key.Escape) Keyboard.ClearFocus();
            }), handledEventsToo: true);
            return box;
        });
    }

    /// <summary>
    /// Makes a cell's value changeable from a list: a click on the cell shows the list, open, the value chosen in it.
    /// </summary>
    /// <param name="cell">The cell, whose child is the value's text block.</param>
    /// <param name="choices">What can be chosen: each code, with the name the list shows.</param>
    /// <param name="chosen">The code chosen now.</param>
    /// <param name="keep">Keeps a code chosen; false when the change was refused.</param>
    public static void WithChoice(Bevel cell, IReadOnlyList<(string Code, string Shown)> choices, string chosen, Func<string, bool> keep)
    {
        MakeClickable(cell, done =>
        {
            var list = new ComboBox { MinWidth = 40 };
            foreach ((string code, string shown) in choices)
            {
                var item = new ComboBoxItem { Content = shown, Tag = code };
                list.Items.Add(item);
                if (code == chosen) list.SelectedItem = item;
            }
            list.DropDownClosed += (_, _) =>
            {
                done();
                if (list.SelectedItem is ComboBoxItem { Tag: string code } && code != chosen) list.Dispatcher.BeginInvoke(() => keep(code));
            };
            list.Loaded += (_, _) => list.IsDropDownOpen = true;
            return list;
        });
    }

    /// <summary>
    /// A click on the cell puts the control made in place of its text, focused; when the control loses the focus (or
    /// says it is done), the text is put back. The pane is drawn again once the change is saved, so the text then shows
    /// the value as changed.
    /// </summary>
    /// <param name="cell">The cell, whose child is the value's text block.</param>
    /// <param name="makeControl">Makes the control, given what puts the text back.</param>
    private static void MakeClickable(Bevel cell, Func<Action, Control> makeControl)
    {
        if (cell.Child is not TextBlock text) return;
        // a transparent background, so a click anywhere in the cell reaches it
        cell.Background ??= Brushes.Transparent;
        cell.Cursor = Cursors.IBeam;
        cell.MouseLeftButtonUp += (_, e) =>
        {
            if (cell.Child is not TextBlock) return;
            e.Handled = true;
            Control? control = null;
            Thickness padding = cell.Padding;
            void PutTextBack()
            {
                if (!ReferenceEquals(cell.Child, control)) return;
                cell.Child = text;
                cell.Padding = padding;
            }
            control = makeControl(PutTextBack);
            // the box nearly fills the cell, so the row grows as little as it can
            cell.Padding = new Thickness(2, 1, 2, 1);
            cell.Child = control;
            control.LostKeyboardFocus += (_, _) =>
            {
                // after the box has kept its value; changing the cell while the focus moves would upset the move. A
                // list keeps its cell while it is open: the focus is in its drop-down then.
                control.Dispatcher.BeginInvoke(() =>
                {
                    if (!control.IsKeyboardFocusWithin && control is not ComboBox { IsDropDownOpen: true }) PutTextBack();
                });
            };
            // focused once it is laid out: before, it cannot take the focus
            control.Loaded += (_, _) =>
            {
                control.Focus();
                if (control is TextBox box) box.SelectAll();
            };
        };
    }

    /// <summary>A box with text that is not a value it takes is outlined in the caution colour.</summary>
    private static void ShowWhetherTaken(TextBox box, Func<string, bool> isTaken)
    {
        if (isTaken(box.Text.Trim())) box.ClearValue(Control.BorderBrushProperty);
        else box.SetResourceReference(Control.BorderBrushProperty, "caution");
    }
}
