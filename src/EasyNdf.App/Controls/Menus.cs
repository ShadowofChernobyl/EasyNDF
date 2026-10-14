using System.Windows;
using System.Windows.Controls;

namespace EasyNdf.App.Controls;

/// <summary>One item of a right-click menu: its text, whether it can be used now, and what it does.</summary>
/// <param name="Label">The text.</param>
/// <param name="Run">What it does.</param>
/// <param name="IsEnabled">Whether it can be used now; a disabled item is shown dim.</param>
/// <param name="Key">The key that does the same, such as "Ctrl+V", shown at the item's right; "" for none.</param>
/// <param name="Tip">The item's tooltip; "" for none.</param>
/// <param name="Symbol">A symbol before the text, such as a rule type's; "" for none.</param>
/// <param name="SymbolColour">The theme colour the symbol is drawn in.</param>
public sealed record MenuChoice(string Label, Action Run, bool IsEnabled = true, string Key = "", string Tip = "", string Symbol = "", string SymbolColour = "ink");

/// <summary>
/// Right-click menus (the HTML app's <c>showMenu</c>), drawn by the ContextMenu style in Styles/Controls.xaml. A null
/// item is left out, and <see cref="Line"/> draws a line between groups.
/// </summary>
public static class Menus
{
    /// <summary>A line between two groups of items.</summary>
    public static readonly MenuChoice Line = new("-", () => { });

    /// <summary>Opens a menu where the mouse is.</summary>
    /// <param name="owner">The element right-clicked.</param>
    /// <param name="items">The items, in order.</param>
    public static void Show(FrameworkElement owner, IEnumerable<MenuChoice?> items)
    {
        ContextMenu menu = Build(items);
        menu.PlacementTarget = owner;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>Opens a menu just above a button, as a menu at the foot of a list opens.</summary>
    public static void ShowAbove(FrameworkElement button, IEnumerable<MenuChoice?> items)
    {
        ContextMenu menu = Build(items);
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.VerticalOffset = -2;
        menu.IsOpen = true;
    }

    /// <summary>Makes a menu without opening it.</summary>
    public static ContextMenu Build(IEnumerable<MenuChoice?> items)
    {
        var menu = new ContextMenu();
        foreach (MenuChoice item in items.OfType<MenuChoice>())
        {
            if (ReferenceEquals(item, Line)) { menu.Items.Add(new Separator()); continue; }
            var entry = new MenuItem { Header = HeaderOf(item), IsEnabled = item.IsEnabled, InputGestureText = item.Key };
            if (item.Tip != "") entry.ToolTip = item.Tip;
            entry.Click += (_, _) => item.Run();
            menu.Items.Add(entry);
        }
        return menu;
    }

    /// <summary>An item's text, after its symbol when it has one (18 pixels wide, in its colour, as in the rule list).</summary>
    private static object HeaderOf(MenuChoice item)
    {
        if (item.Symbol == "") return item.Label;
        FrameworkElement symbol = FolderGlyph.For(item.Symbol, item.SymbolColour, 18);
        symbol.Margin = new Thickness(0, 0, 6, 0);
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { symbol, new TextBlock { Text = item.Label } } };
    }
}
