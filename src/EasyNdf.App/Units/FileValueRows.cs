using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;

namespace EasyNdf.App.Units;

/// <summary>
/// One name in a Files section: its label, the name as the game's files write it, and, for a descriptor or file, what
/// shows it in the File Editor.
/// </summary>
/// <param name="Label">The label, such as "Descriptor".</param>
/// <param name="Value">The name, such as "Descriptor_Unit_M48_Chaparral_MIM72F_US".</param>
/// <param name="Open">Shows the name in the File Editor; null for a name that can only be copied.</param>
/// <param name="CanOpen">Whether the file it is in is on the Files tab, so <paramref name="Open"/> can show it.</param>
/// <param name="File">The file it is in, named when it is not on the Files tab.</param>
public sealed record FileValue(string Label, string Value, Action? Open = null, bool CanOpen = false, string File = "");

/// <summary>
/// The Files sections of the Division, Unit and Weapon panes: a label at the left, dim, and the name at its right in
/// the code font and the headings' colour, broken over lines when long. The names are not boxes, as they cannot be
/// changed there: a click shows a descriptor or file in the File Editor when its file is on the Files tab, and copies
/// any other name; right-click offers both.
/// </summary>
public static class FileValueRows
{
    /// <summary>The rows, one under the other.</summary>
    public static Grid Build(IEnumerable<FileValue> values)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (FileValue value in values)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = UnitPane.Muted(value.Label, new Thickness(0, 1, 10, 1));
            TextBlock text = Value(value);
            Grid.SetRow(label, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(label);
            grid.Children.Add(text);
        }
        return grid;
    }

    /// <summary>The name: underlined under the mouse, as a link is; a click opens or copies it, right-click has both.</summary>
    private static TextBlock Value(FileValue value)
    {
        bool opens = value.Open is not null && value.CanOpen;
        var text = new TextBlock
        {
            Text = value.Value, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand,
            ToolTip = opens ? Texts.Get("Units.Pane.FileValue.Open.Tip") : Texts.Get("Units.Pane.FileValue.Copy.Tip"),
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        text.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        text.MouseEnter += (_, _) => text.TextDecorations = TextDecorations.Underline;
        text.MouseLeave += (_, _) => text.TextDecorations = null;
        text.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (opens) value.Open!();
            else Copy(value.Value);
        };
        text.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Menus.Show(text, Menu(value));
        };
        return text;
    }

    /// <summary>Right-click: copy the name, and show it in the File Editor when it is a descriptor or file.</summary>
    private static IEnumerable<MenuChoice?> Menu(FileValue value) =>
    [
        new MenuChoice(Texts.Get("Common.Menu.Copy"), () => Copy(value.Value)),
        value.Open is null ? null : new MenuChoice(Texts.Get("Units.Pane.ShowInEditor"), value.Open, IsEnabled: value.CanOpen,
            Tip: value.CanOpen ? "" : Texts.Get("Units.Menu.NotLoaded.Tip", ("file", value.File))),
    ];

    /// <summary>Puts text on the clipboard; another program holding the clipboard just leaves it as it was.</summary>
    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }
}
