using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>The Units tab's views: Cards, Table and Compare, and the Unit pane.</summary>
public partial class UnitsPage
{
    /// <summary>The room a vertical scrollbar takes, kept free so the tiles and columns do not move when it shows.</summary>
    private const double ScrollBarWidth = 14;

    /// <summary>The units the filters leave, in the order of the game's file.</summary>
    private List<Unit> shown = [];

    /// <summary>The unit selected, shown in the Unit pane.</summary>
    private readonly UnitSelection selection = new();

    /// <summary>The roles folded in the Cards view, by code.</summary>
    private readonly HashSet<string> foldedRoles = [];

    /// <summary>How many tiles a row of the Cards view had when it was last filled.</summary>
    private int cardColumns;

    /// <summary>The units on Compare, in the order they were added.</summary>
    private readonly List<Unit> compared = [];

    /// <summary>The unit the others on Compare are measured from: the first added, until another is pinned.</summary>
    private Unit? pinned;

    // ---- Cards ----

    /// <summary>
    /// The Cards view: the units in groups by role, the biggest group first, each a heading and rows of tiles, as many
    /// across as fit.
    /// </summary>
    private void ShowCards()
    {
        cardColumns = CardRowView.ColumnsFor(CardsRoom());
        var lines = new List<CardLine>();
        IEnumerable<IGrouping<string, Unit>> groups = shown.GroupBy(unit => unit.Role).OrderByDescending(group => group.Count());
        foreach (IGrouping<string, Unit> group in groups)
        {
            List<Unit> units = [.. group];
            bool folded = foldedRoles.Contains(group.Key);
            lines.Add(new CardGroupLine(group.Key, UnitNames.RoleOf(units[0]), units.Count, folded));
            if (folded) continue;
            for (int start = 0; start < units.Count; start += cardColumns)
            {
                List<Unit> row = units.GetRange(start, Math.Min(cardColumns, units.Count - start));
                lines.Add(new CardRowLine(row, cardColumns, IsLast: start + cardColumns >= units.Count, selection, this));
            }
        }
        if (lines.Count == 0) lines.Add(new EmptyLine(Texts.Get("Units.NoneMatch")));
        CardsList.ItemsSource = lines;
    }

    /// <summary>The width the cards have across, beside the scrollbar, in the rows' own units: less when zoomed in.</summary>
    private double CardsRoom() => (CardsList.ActualWidth - ScrollBarWidth) / ScaleOf(UnitView.Cards);

    /// <summary>The list was resized: the rows are made again when a different number of tiles fits across.</summary>
    private void CardsListResized(object sender, SizeChangedEventArgs e)
    {
        if (loaded is null || view != UnitView.Cards) return;
        if (CardRowView.ColumnsFor(CardsRoom()) != cardColumns) ShowCards();
    }

    /// <summary>
    /// In the Cards view, Ctrl+Shift+[ folds every role's tiles away and Ctrl+Shift+] brings them all back, as on the
    /// Rules tab; not while typing in a box.
    /// </summary>
    private void FoldKeys(object sender, KeyEventArgs e)
    {
        if (view != UnitView.Cards || loaded is null || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (RulesPage.IsCollapseAllKey(e))
        {
            e.Handled = true;
            foldedRoles.UnionWith(shown.Select(unit => unit.Role));
            ShowCards();
        }
        else if (RulesPage.IsExpandAllKey(e))
        {
            e.Handled = true;
            foldedRoles.Clear();
            ShowCards();
        }
    }

    /// <summary>A role's heading clicked: its tiles fold away or come back.</summary>
    private void GroupClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CardGroupLine group }) return;
        e.Handled = true;
        if (!foldedRoles.Remove(group.Role)) foldedRoles.Add(group.Role);
        ShowCards();
    }

    // ---- Table ----

    /// <summary>The Table view: one row a unit, with the Units table's own columns.</summary>
    private void ShowTable() =>
        TableList.Show(shown, UnitTableColumn.All, Game, selection, this, Texts.Get("Units.NoneMatch"));

    // ---- a unit shown from another tab ----

    /// <summary>
    /// "Show in Units Tab" on the Divisions tab: the unit selected in Cards or Table, and scrolled to. When the filters
    /// hide it, they are cleared, with its category's tab chosen; its role's tiles are unfolded.
    /// </summary>
    public void ShowUnit(Unit unit)
    {
        if (loaded is null) return;
        if (view == UnitView.Compare)
        {
            view = UnitView.Cards;
            ShowViewButtons();
        }
        if (!filter.Apply(loaded.Catalog.Units).Contains(unit))
        {
            ResetFilters();
            // a unit in none of the game's divisions (one a mod adds) shows only with "Only units in a division" off
            filter.OnlyInDivision = unit.Divisions.Count > 0;
            OnlyInDivision.IsChecked = filter.OnlyInDivision;
            filter.Factory = unit.Factory;
        }
        foldedRoles.Remove(unit.Role);
        ShowAll();
        selection.Selected = unit;
        // once the tab is on screen and the list laid out
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ScrollTo(unit));
    }

    /// <summary>Scrolls Cards or Table to the line that holds the unit.</summary>
    private void ScrollTo(Unit unit)
    {
        ItemsControl list = view == UnitView.Table ? TableList : CardsList;
        if (list.ItemsSource is not System.Collections.IList lines) return;
        int index = -1;
        for (int at = 0; at < lines.Count && index < 0; at++)
            if (lines[at] is CardRowLine row && row.Units.Contains(unit) || lines[at] is TableRowLine tableRow && ReferenceEquals(tableRow.Unit, unit)) index = at;
        if (index >= 0 && FindPanel(list) is VirtualizingStackPanel panel) panel.BringIndexIntoViewPublic(index);
    }

    /// <summary>The panel that lays out a list's lines, found in what the list drew.</summary>
    private static VirtualizingStackPanel? FindPanel(DependencyObject inside)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(inside); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(inside, i);
            if (child is VirtualizingStackPanel panel) return panel;
            if (FindPanel(child) is VirtualizingStackPanel found) return found;
        }
        return null;
    }

    // ---- clicks on the lists ----

    /// <summary>The unit under the mouse in the Cards or Table view; null over a heading or empty room.</summary>
    private static Unit? UnitAt(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is UnitTile tile) return tile.Unit;
            if (at is UnitTableRowView row) return row.Unit;
        }
        return null;
    }

    /// <summary>A click on a unit selects it, to show it in the Unit pane.</summary>
    private void UnitListClicked(object sender, MouseButtonEventArgs e)
    {
        if (UnitAt(e.OriginalSource) is Unit unit) selection.Selected = unit;
    }

    /// <summary>A double-click on a unit shows it in the File Editor, when its file is on the Files tab.</summary>
    private void UnitListDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || UnitAt(e.OriginalSource) is not Unit unit || !CanShowInEditor(unit)) return;
        e.Handled = true;
        ShowInEditor(unit);
    }

    /// <summary>A right-click on a unit opens its menu; on the list's empty room, the menu for the units shown.</summary>
    private void UnitListRightClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (UnitAt(e.OriginalSource) is Unit unit)
        {
            selection.Selected = unit;
            OpenUnitMenu(unit, (FrameworkElement)sender);
        }
        else OpenListMenu((FrameworkElement)sender);
    }

    // ---- the Unit pane ----

    /// <summary>The unit the Unit pane shows.</summary>
    private Unit? paneUnit;

    /// <summary>The selected unit in the Unit pane; scrolled to its top when it is another unit than before.</summary>
    private void ShowUnitPane()
    {
        if (loaded is null) return;
        UnitPaneContent.Content = UnitPane.Build(selection.Selected, this);
        if (!ReferenceEquals(paneUnit, selection.Selected)) UnitPaneScroller.ScrollToTop();
        paneUnit = selection.Selected;
    }

    // ---- Compare ----

    /// <summary>
    /// Compare: a card a unit, the pinned unit first and the others in the order they were added. Each stat's best
    /// value, and the longest weapon range, are marked across all the cards.
    /// </summary>
    private void ShowCompare()
    {
        CompareList.Children.Clear();
        if (pinned is null || !compared.Contains(pinned)) pinned = compared.FirstOrDefault();
        if (pinned is null)
        {
            var empty = new TextBlock { Text = Texts.Get("Units.Compare.Nothing"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 10) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            CompareList.Children.Add(empty);
            return;
        }
        Dictionary<string, double?> best = UnitStat.All.ToDictionary(stat => stat.Key, stat => stat.Best(compared));
        double? bestRange = compared.SelectMany(unit => unit.Weapons).Select(weapon => weapon.Range).Where(range => range > 0).DefaultIfEmpty().Max();
        foreach (Unit unit in compared.OrderBy(unit => ReferenceEquals(unit, pinned) ? 0 : 1))
        {
            Unit removed = unit;
            CompareList.Children.Add(new CompareCard(unit, pinned, best, bestRange, this, () => RemoveFromCompare(removed)) { Margin = new Thickness(0, 0, 0, 8) });
        }
    }

    private void RemoveFromCompare(Unit unit)
    {
        compared.Remove(unit);
        ShowAll();
    }

    /// <summary>"Pin for Comparison": the others on Compare are measured from this unit, which goes first.</summary>
    private void Pin(Unit unit)
    {
        pinned = unit;
        ShowAll();
    }

    /// <summary>Units added to Compare, after those there; Compare is shown.</summary>
    private void AddToCompare(IEnumerable<Unit> units)
    {
        foreach (Unit unit in units)
            if (!compared.Contains(unit)) compared.Add(unit);
        view = UnitView.Compare;
        ShowViewButtons();
        ShowAll();
    }

    /// <summary>"+ Add Units…": the window to pick units for Compare, those already there greyed.</summary>
    private void AddUnitsClicked(object sender, RoutedEventArgs e)
    {
        if (loaded is null) return;
        MainView.Of(this).AddUnits.Open(Texts.Get("Units.AddUnits.Title"), loaded.Catalog.Units, Game, compared.ToHashSet(), AddToCompare);
    }
}
