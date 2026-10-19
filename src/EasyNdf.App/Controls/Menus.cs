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
/// <param name="IsHeading">A heading at the top of the menu rather than an item (see <see cref="Menus.Heading"/>).</param>
/// <param name="Choices">The items of a menu of its own, opened at its right; null for an item that does something.</param>
public sealed record MenuChoice(string Label, Action Run, bool IsEnabled = true, string Key = "", string Tip = "", string Symbol = "", string SymbolColour = "ink", bool IsHeading = false,
    IReadOnlyList<MenuChoice?>? Choices = null)
{
    /// <summary>An item that opens a menu of its own, at its right, with <paramref name="choices"/>.</summary>
    public static MenuChoice Submenu(string label, IReadOnlyList<MenuChoice?> choices) =>
        new(label, () => { }, IsEnabled: choices.Any(choice => choice is not null), Choices: choices);
}

/// <summary>
/// Right-click menus (the HTML app's <c>showMenu</c>), drawn by the ContextMenu style in Styles/Controls.xaml. A null
/// item is left out, and <see cref="Line"/> draws a line between groups.
/// </summary>
public static class Menus
{
    /// <summary>A line between two groups of items.</summary>
    public static readonly MenuChoice Line = new("-", () => { });

    /// <summary>
    /// A heading at the top of a menu, naming what it is for (#ctx .hd), such as the weapon right-clicked: small, dim,
    /// and not an item to choose.
    /// </summary>
    public static MenuChoice Heading(string text) => new(text, () => { }, IsEnabled: false, IsHeading: true);

    /// <summary>Opens a menu where the mouse is, and gives it back.</summary>
    /// <param name="owner">The element right-clicked.</param>
    /// <param name="items">The items, in order.</param>
    public static ContextMenu Show(FrameworkElement owner, IEnumerable<MenuChoice?> items)
    {
        ContextMenu menu = Build(items);
        menu.PlacementTarget = owner;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        return menu;
    }

    /// <summary>Opens a menu 2 pixels under a button, such as a ▾, and gives it back.</summary>
    public static ContextMenu ShowBelow(FrameworkElement button, IEnumerable<MenuChoice?> items)
    {
        ContextMenu menu = Build(items);
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.VerticalOffset = 2;
        menu.IsOpen = true;
        return menu;
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
        AddItems(menu.Items, items);
        return menu;
    }

    /// <summary>Adds the items to a menu, or to an item's own menu.</summary>
    private static void AddItems(ItemCollection into, IEnumerable<MenuChoice?> items)
    {
        foreach (MenuChoice item in items.OfType<MenuChoice>())
        {
            if (ReferenceEquals(item, Line)) { into.Add(new Separator()); continue; }
            if (item.IsHeading) { into.Add(HeadingItem(item.Label)); continue; }
            var entry = new MenuItem { Header = HeaderOf(item), IsEnabled = item.IsEnabled, InputGestureText = Text.KeyNames.Of(item.Key) };
            if (item.Tip != "") entry.ToolTip = item.Tip;
            // a dim item's tip says why it is dim, such as which file to add first
            ToolTipService.SetShowOnDisabled(entry, true);
            if (item.Choices is not null) AddItems(entry.Items, item.Choices);
            else entry.Click += (_, _) => item.Run();
            into.Add(entry);
        }
    }

    /// <summary>A heading's line: 11 pixels, dim, on one line; the mouse and the keys pass over it.</summary>
    private static MenuItem HeadingItem(string text)
    {
        var heading = new TextBlock { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = text };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new MenuItem { Header = heading, Focusable = false, IsHitTestVisible = false };
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
