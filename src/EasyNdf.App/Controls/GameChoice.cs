using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Controls;

/// <summary>
/// A dropdown of the games (<see cref="GameTitle.All"/>), by name: the game of a new mod, or of files added by hand. A
/// dropdown rather than a switch, as more games may come.
/// </summary>
public sealed class GameChoice : ComboBox
{
    /// <summary>Lists every game, with <paramref name="chosen"/> chosen.</summary>
    public GameChoice(GameTitle chosen)
    {
        // the app's own dropdown look: a class made from ComboBox does not take the style made for ComboBox by itself
        SetResourceReference(StyleProperty, typeof(ComboBox));
        foreach (GameTitle game in GameTitle.All) Items.Add(new ComboBoxItem { Content = game.Name, Tag = game });
        SelectedIndex = Math.Max(0, GameTitle.All.ToList().IndexOf(chosen));
        MinWidth = 160;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    /// <summary>The game chosen.</summary>
    public GameTitle Chosen => (SelectedItem as ComboBoxItem)?.Tag as GameTitle ?? GameTitle.Warno;

    /// <summary>The dropdown on a line of its own, after its label ("Game"), as a message window shows it under its question.</summary>
    /// <param name="label">The label before it.</param>
    /// <param name="tip">The dropdown's tooltip, saying what the game changes.</param>
    public FrameworkElement Labelled(string label, string tip)
    {
        ToolTip = tip;
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { name, this } };
    }

    /// <summary>The game a new mod, or files added by hand, start with: the Source's game when one is chosen, else the open mod's.</summary>
    /// <param name="sourceGame">The game of the Source chosen on the Files tab, or null when none is.</param>
    public static GameTitle StartingGame(GameTitle? sourceGame) => sourceGame ?? AppData.Current.OpenMod.Game;

    /// <summary>The words before the dropdown: "Game:", as the other labels of the message and rule windows.</summary>
    public static string Label => Texts.WithColon(Texts.Get("Common.Game"));
}
