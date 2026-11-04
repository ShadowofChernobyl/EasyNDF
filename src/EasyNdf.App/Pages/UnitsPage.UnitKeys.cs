using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.App.Weapons;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The units chosen with the keyboard: Tab reaches the Cards or Table view (or a division's cards, or its battlegroup)
/// as one stop, the arrow keys select the unit beside, above or below (Shift with them selects those between), Home and
/// End the first and last, Enter picks the unit alone (as a click), Space adds it to those selected or takes it away (as
/// Ctrl+Click), and the menu key opens its menu. The views make only the tiles in sight, so the keys move through the units shown, not through tiles.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The views that take the keys; called once, as the tab is built.</summary>
    private void PrepareUnitKeys()
    {
        foreach (Control list in new Control[] { CardsList, TableList, DivisionCardsScroller, BattlegroupScroller })
        {
            list.Focusable = true;
            list.IsTabStop = true;
            list.PreviewKeyDown += UnitListKeys;
            System.Windows.Automation.AutomationProperties.SetName(list, Texts.Get("Armory.Units"));
        }
    }

    /// <summary>A key in a view of units: the unit beside, above or below is selected and brought into view.</summary>
    private void UnitListKeys(object sender, KeyEventArgs e)
    {
        // a box of the table keeps its own keys
        if (!ReferenceEquals(e.OriginalSource, sender) || loaded is null) return;
        if (Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        IReadOnlyList<Unit> units = UnitsInOrder();
        if (units.Count == 0) return;
        int at = selection.Selected is Unit current ? IndexIn(units, current) : -1;
        if (e.Key is Key.Space or Key.Enter or Key.Apps)
        {
            e.Handled = true;
            if (at < 0)
            {
                selection.Selected = units[0];
                BringUnitIntoView(units[0]);
                return;
            }
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None) selection.Toggle(units[at]);
            else if (e.Key == Key.Apps) OpenUnitMenu(units[at], (FrameworkElement)sender);
            // Enter picks the unit alone, as a plain click does
            else if (e.Key == Key.Enter) selection.Selected = units[at];
            return;
        }
        int? to = e.Key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Up => RowStep(at, up: true),
            Key.Down => RowStep(at, up: false),
            Key.Home => 0,
            Key.End => units.Count - 1,
            _ => null,
        };
        if (to is not int wanted) return;
        e.Handled = true;
        Unit unit = units[at < 0 ? 0 : Math.Clamp(wanted, 0, units.Count - 1)];
        if (Keyboard.Modifiers == ModifierKeys.Shift) selection.SelectRange(units, unit);
        else selection.Selected = unit;
        BringUnitIntoView(unit);
    }

    /// <summary>Where a unit is in a list, by reference: two units can be alike.</summary>
    private static int IndexIn(IReadOnlyList<Unit> units, Unit unit)
    {
        for (int index = 0; index < units.Count; index++)
            if (ReferenceEquals(units[index], unit)) return index;
        return -1;
    }

    /// <summary>
    /// The unit above or below one, by its place among the units shown: in the same place of the row of tiles above or
    /// below, or the last of that row when it is shorter; in the table, the row above or below.
    /// </summary>
    private int RowStep(int at, bool up)
    {
        if (at < 0) return 0;
        List<IReadOnlyList<Unit>> rows = UnitRows();
        int first = 0;
        for (int row = 0; row < rows.Count; row++)
        {
            if (at < first + rows[row].Count)
            {
                int place = at - first;
                int next = row + (up ? -1 : 1);
                if (next < 0 || next >= rows.Count) return at;
                int nextFirst = up ? first - rows[next].Count : first + rows[row].Count;
                return nextFirst + Math.Min(place, rows[next].Count - 1);
            }
            first += rows[row].Count;
        }
        return at;
    }

    /// <summary>The units shown in their rows of tiles, in order; in the table, a row a unit.</summary>
    private List<IReadOnlyList<Unit>> UnitRows()
    {
        var rows = new List<IReadOnlyList<Unit>>();
        if (!ShowsBattlegroup && view == UnitView.Table)
        {
            foreach (Unit unit in TableList.UnitsInOrder) rows.Add([unit]);
            return rows;
        }
        if (!ShowsBattlegroup && !ShowsDivisionUnits)
        {
            if (CardsList.ItemsSource is IEnumerable<CardLine> lines) rows.AddRange(lines.OfType<CardRowLine>().Select(line => line.Units));
            return rows;
        }
        // a division's cards and a battlegroup's: a grid of tiles a type, cut into its rows
        IReadOnlyList<Unit> units = ShowsBattlegroup ? BattlegroupTilesInOrder() : DivisionTilesInOrder();
        object? content = ShowsBattlegroup ? BattlegroupContent.Content : DivisionCardsContent.Content;
        int done = 0;
        if (content is StackPanel groups)
            foreach (UniformGrid grid in groups.Children.OfType<UniformGrid>())
            {
                int count = grid.Children.OfType<FrameworkElement>().Count(child => child is UnitTile or Divisions.LazyTile || child is Border { Child: UnitTile });
                int across = Math.Max(1, grid.Columns);
                for (int start = 0; start < count && done + start < units.Count; start += across)
                    rows.Add([.. units.Skip(done + start).Take(Math.Min(across, count - start))]);
                done += count;
            }
        return rows;
    }

    /// <summary>The unit's tile or row scrolled into view; the views make only those in sight, so a far one is made first.</summary>
    private void BringUnitIntoView(Unit unit)
    {
        if (!ShowsBattlegroup && view == UnitView.Table)
        {
            TableList.ScrollToUnit(unit);
            return;
        }
        if (!ShowsBattlegroup && !ShowsDivisionUnits)
        {
            if (CardsList.ItemsSource is not List<CardLine> lines) return;
            int line = lines.FindIndex(each => each is CardRowLine row && row.Units.Any(shownUnit => ReferenceEquals(shownUnit, unit)));
            if (line >= 0 && WeaponsArmory.VisualsIn<Controls.LinesPanel>(CardsList).FirstOrDefault() is Controls.LinesPanel panel) panel.BringIndexIntoViewPublic(line);
            return;
        }
        FrameworkElement holder = ShowsBattlegroup ? BattlegroupScroller : DivisionCardsScroller;
        // a division's tile not made yet: its place is brought into view, which makes it
        if (!ShowsBattlegroup && WeaponsArmory.VisualsIn<Divisions.LazyTile>(holder).FirstOrDefault(place => ReferenceEquals(place.Unit, unit)) is Divisions.LazyTile place)
        {
            place.Make();
            place.BringIntoView();
            return;
        }
        WeaponsArmory.VisualsIn<UnitTile>(holder).FirstOrDefault(tile => ReferenceEquals(tile.Unit, unit))?.BringIntoView();
    }
}
