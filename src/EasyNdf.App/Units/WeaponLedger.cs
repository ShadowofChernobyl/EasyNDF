using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// A unit's weapons as a table (the "Loadout Ledger"): grouped by sort (anti-armor, missiles...), one row a weapon with
/// its icon, how many the unit has and its name, then its range, damage, suppression and seconds between salvos, as
/// numbers only. Compare shows it wide; the Unit pane narrow, with short headings whose tooltips give them in full.
/// Right-clicking a weapon, or its ▾ (shown under the mouse), opens its menu.
/// </summary>
public static class WeaponLedger
{
    /// <summary>The number columns, which are as wide as their widest number and line up across the table's rows.</summary>
    private static readonly string[] NumberColumns = ["Range", "Damage", "Suppression", "Salvo"];

    /// <summary>Builds the table.</summary>
    /// <param name="unit">The unit whose weapons are shown.</param>
    /// <param name="rangeUnit">The game's unit of range, for the Range heading: "GRU" or "m".</param>
    /// <param name="narrow">The Unit pane's narrow table: smaller text, tighter cells, short headings.</param>
    /// <param name="bestRange">On Compare, the longest range among the units compared, drawn in the "best" colour; null for none.</param>
    /// <param name="openMenu">Opens a weapon's menu and gives it back: at the mouse over the row, or under the ▾ button.</param>
    public static FrameworkElement Build(Unit unit, string rangeUnit, bool narrow, double? bestRange, Func<UnitWeapon, FrameworkElement, ContextMenu> openMenu)
    {
        if (unit.Weapons.Count == 0)
        {
            var none = new TextBlock { Text = Texts.Get("Units.NoWeapons") };
            none.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            return none;
        }
        var rows = new StackPanel();
        // the number columns line up across the rows, each its own grid, as each row needs its own hover colour
        Grid.SetIsSharedSizeScope(rows, true);
        rows.Children.Add(HeadingRow(rangeUnit, narrow));
        foreach ((WeaponKind kind, List<(UnitWeapon Weapon, int Index)> weapons) in WeaponKinds.Grouped(unit.Weapons))
        {
            rows.Children.Add(GroupRow(kind));
            foreach ((UnitWeapon weapon, _) in weapons)
                rows.Children.Add(WeaponRow(weapon, narrow, bestRange, openMenu));
        }
        var table = new Bevel { Child = rows };
        table.SetResourceReference(Bevel.TopLeftProperty, "lo");
        table.SetResourceReference(Bevel.BottomRightProperty, "hi");
        table.SetResourceReference(Bevel.BackgroundProperty, "well");
        return table;
    }

    /// <summary>A grid with the name column taking the room left and the number columns as wide as their widest cell.</summary>
    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (string column in NumberColumns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = column });
        return grid;
    }

    /// <summary>
    /// The headings, raised on the panel colour, dim. The narrow table's are short (Dmg, Supp.) with the full words as
    /// tooltips; the wide table's are written out, with tooltips where a word needs explaining.
    /// </summary>
    private static Grid HeadingRow(string rangeUnit, bool narrow)
    {
        Grid grid = RowGrid();
        string range = Texts.Get("Units.Weapons.RangeIn", ("unit", rangeUnit));
        AddHeading(grid, 0, Texts.Get("Units.Weapons.Weapon"), "", narrow, alignRight: false);
        AddHeading(grid, 1, narrow ? Texts.Get("Units.Weapons.Range") : range, narrow ? range : "", narrow, alignRight: true);
        AddHeading(grid, 2, Texts.Get(narrow ? "Units.Weapons.Damage.Short" : "Units.Weapons.Damage"), Texts.Get("Units.Weapons.Damage.Tip"), narrow, alignRight: true);
        AddHeading(grid, 3, Texts.Get(narrow ? "Units.Weapons.Suppression.Short" : "Units.Weapons.Suppression"), narrow ? Texts.Get("Units.Weapons.Suppression") : "", narrow, alignRight: true);
        AddHeading(grid, 4, Texts.Get("Units.Weapons.Salvo"), Texts.Get("Units.Weapons.Salvo.Tip"), narrow, alignRight: true);
        return grid;
    }

    private static void AddHeading(Grid grid, int column, string text, string tip, bool narrow, bool alignRight)
    {
        var heading = new TextBlock { Text = text, FontSize = narrow ? 10.5 : 11, TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var cell = new Bevel { Child = heading, Padding = narrow ? new Thickness(3, 2, 3, 2) : new Thickness(6, 2, 6, 2) };
        cell.SetResourceReference(Bevel.TopLeftProperty, "hi");
        cell.SetResourceReference(Bevel.BottomRightProperty, "lo");
        cell.SetResourceReference(Bevel.BackgroundProperty, "panel");
        if (tip != "") cell.ToolTip = tip;
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    /// <summary>A group's heading: its name in capitals, small, bold, in the accent colour, on the panel colour.</summary>
    private static Bevel GroupRow(WeaponKind kind)
    {
        var name = new SpacedText { Text = UnitNames.WeaponGroup(kind).ToUpper(Texts.Catalog.Culture), Spacing = 0.04 };
        name.SetValue(TextElement.FontSizeProperty, 10.0);
        name.SetValue(TextElement.FontWeightProperty, FontWeights.Bold);
        name.SetResourceReference(TextElement.ForegroundProperty, "accent");
        var row = new Bevel { Child = name, Padding = new Thickness(6, 1, 6, 1), Edges = new Thickness(0, 1, 0, 0) };
        row.SetResourceReference(Bevel.TopLeftProperty, "hi");
        row.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return row;
    }

    /// <summary>
    /// A weapon's row: its icon, "2×" when it has more than one, its name (wrapping), the ▾ for its menu, then its
    /// numbers. Under the mouse the row takes the selection colour, and the hover colour while its menu is open.
    /// </summary>
    private static Bevel WeaponRow(UnitWeapon weapon, bool narrow, double? bestRange, Func<UnitWeapon, FrameworkElement, ContextMenu> openMenu)
    {
        Grid grid = RowGrid();
        grid.Children.Add(NameCell(weapon, narrow, out Button menuButton));
        bool isBest = bestRange is double best && weapon.Range == best;
        AddNumber(grid, 1, weapon.Range is > 0 ? UnitNumbers.Of(weapon.Range) : UnitNumbers.None, narrow, isBest);
        AddNumber(grid, 2, UnitNumbers.Of(weapon.Damage), narrow, isBest: false);
        AddNumber(grid, 3, UnitNumbers.Of(weapon.Suppression, decimals: 0), narrow, isBest: false);
        AddNumber(grid, 4, UnitNumbers.Seconds(weapon.SalvoSeconds, narrow), narrow, isBest: false);

        var row = new Bevel { Child = grid, Edges = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Arrow };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        bool menuOpen = false;
        void Colour()
        {
            if (menuOpen) row.SetResourceReference(Bevel.BackgroundProperty, "hover");
            else if (row.IsMouseOver) row.SetResourceReference(Bevel.BackgroundProperty, "sel");
            else row.Background = Brushes.Transparent;
            menuButton.Visibility = menuOpen || row.IsMouseOver ? Visibility.Visible : Visibility.Hidden;
        }
        row.MouseEnter += (_, _) => Colour();
        row.MouseLeave += (_, _) => Colour();
        void Open(FrameworkElement at)
        {
            ContextMenu menu = openMenu(weapon, at);
            // the row keeps the hover colour, and its ▾ shows, until its menu closes
            menuOpen = true;
            Colour();
            menu.Closed += (_, _) => { menuOpen = false; Colour(); };
        }
        row.MouseRightButtonUp += (_, e) => { e.Handled = true; Open(row); };
        menuButton.Click += (_, _) => Open(menuButton);
        return row;
    }

    /// <summary>The name cell: the sort's icon, the count in the accent colour, the name, and the ▾ at the right.</summary>
    private static DockPanel NameCell(UnitWeapon weapon, bool narrow, out Button menuButton)
    {
        WeaponKind kind = WeaponKinds.Of(weapon);
        double iconSize = narrow ? 13 : 15;
        var icon = new Path
        {
            Data = UnitShapes.IconOf(kind), Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform, StrokeThickness = 1.6 * iconSize / 18,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, narrow ? 4 : 6, 0), ToolTip = UnitNames.WeaponGroup(kind),
        };
        icon.SetResourceReference(Shape.StrokeProperty, "muted");
        DockPanel.SetDock(icon, Dock.Left);

        menuButton = new Button
        {
            Content = "▾", FontSize = 10, Padding = new Thickness(4, 0, 4, 0), MinHeight = 16, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(narrow ? 4 : 6, 0, 0, 0), Visibility = Visibility.Hidden, ToolTip = Texts.Get("Units.Weapons.Menu.Tip"),
        };
        DockPanel.SetDock(menuButton, Dock.Right);

        var name = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = (narrow ? 11.5 : 12) * 1.2, ToolTip = weapon.Name, FontSize = narrow ? 11.5 : 12 };
        if (weapon.Count > 1)
        {
            var count = new Run(Texts.Get("Units.Weapons.Count", ("count", weapon.Count))) { FontWeight = FontWeights.Bold };
            count.SetResourceReference(TextElement.FontFamilyProperty, "mono");
            count.SetResourceReference(TextElement.ForegroundProperty, "accent");
            name.Inlines.Add(count);
            name.Inlines.Add(new Run(" "));
        }
        name.Inlines.Add(new Run(weapon.Name));

        var cell = new DockPanel { Margin = narrow ? new Thickness(4, 2, 4, 2) : new Thickness(6, 2, 6, 2), Children = { icon, menuButton, name } };
        return cell;
    }

    /// <summary>A number, right-aligned in the code font; the best range bold in the "best" colour.</summary>
    private static void AddNumber(Grid grid, int column, string text, bool narrow, bool isBest)
    {
        var number = new TextBlock
        {
            Text = text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = narrow ? 11 : 12,
            Margin = narrow ? new Thickness(4, 2, 4, 2) : new Thickness(12, 2, 6, 2), FontWeight = isBest ? FontWeights.Bold : FontWeights.Normal,
        };
        number.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
        number.SetResourceReference(TextBlock.ForegroundProperty, isBest ? "ok" : "ink");
        Grid.SetColumn(number, column);
        grid.Children.Add(number);
    }
}
