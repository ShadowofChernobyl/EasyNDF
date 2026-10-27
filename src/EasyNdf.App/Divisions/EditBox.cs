using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A box to change one value of a division on the Divisions tab: a unit's cards, units per card or veterancy share in
/// the table, or the division's name, points or a slot's cost in its pane. What is typed is kept on Enter or when the
/// box is left; Escape puts the value back, as does a value the box does not take.
/// </summary>
public sealed class EditBox : TextBox
{
    private string shown;
    private readonly Func<string, bool> keep;

    /// <summary>Builds the box.</summary>
    /// <param name="shown">The value as shown now.</param>
    /// <param name="width">How wide the box is; NaN to fill its place.</param>
    /// <param name="keep">Keeps what is typed; false when it is not a value the box takes, which puts the value back.</param>
    public EditBox(string shown, double width, Func<string, bool> keep)
    {
        this.shown = shown;
        this.keep = keep;
        // the app's text box look: a style set for TextBox does not reach a class made from it by itself
        SetResourceReference(StyleProperty, typeof(TextBox));
        Text = shown;
        Width = width;
        // numbers in the middle of their small box, as in the mock; a text fills its place from the left
        TextAlignment = double.IsNaN(width) ? TextAlignment.Left : TextAlignment.Center;
        Padding = new Thickness(2, 0, 2, 0);
        KeyDown += KeyPressed;
        LostKeyboardFocus += (_, _) => Keep();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Keep();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Text = shown;
        }
    }

    /// <summary>
    /// Keeps what is typed when it changed; puts the value back when it is not one the box takes, or the change was
    /// refused. A value kept is the box's value from then on, so leaving the box after Enter does not keep it again.
    /// </summary>
    private void Keep()
    {
        string typed = Text.Trim();
        if (typed == shown) return;
        if (keep(typed)) shown = typed;
        else Text = shown;
    }
}
