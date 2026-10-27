using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Divisions;
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

    /// <summary>The units selected; the one clicked last is shown in the Unit pane.</summary>
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
                lines.Add(new CardRowLine(row, cardColumns, IsLast: start + cardColumns >= units.Count, selection, ((IUnitActions)this).IsFirstSide));
            }
        }
        if (lines.Count == 0) lines.Add(new EmptyLine(Texts.Get("Units.NoneMatch")));
        CardsList.ItemsSource = lines;
    }

    /// <summary>The width the units' cards have across, beside the scrollbar, in the rows' own units: less when zoomed in.</summary>
    private double CardsRoom() => RoomIn(CardsList);

    /// <summary>The width a view's tiles have across, beside the scrollbar, in their own units: less when zoomed in.</summary>
    private static double RoomIn(FrameworkElement shownView) => (shownView.ActualWidth - ScrollBarWidth) / ScaleOf(UnitView.Cards);

    /// <summary>
    /// Counts the views waiting to be drawn until they have their width (see <see cref="DrawWhenLaidOut"/>), so only
    /// the last one asked for is drawn.
    /// </summary>
    private int drawsAsked;

    /// <summary>Whether a view just shown waits to be drawn until it has its width: its resizing then draws nothing.</summary>
    private bool drawIsWaiting;

    /// <summary>
    /// Draws a view, at once when it was showing already; a view just shown (on another sub-tab) has no width until the
    /// window is laid out again, with the panes beside it shown or hidden, so it is drawn just after that, once. Drawn
    /// at once, it was drawn for the wrong width, then again when it got its own.
    /// </summary>
    private void DrawWhenLaidOut(FrameworkElement shownView, bool wasHidden, Action draw)
    {
        if (!wasHidden && shownView.ActualWidth > 0)
        {
            drawIsWaiting = false;
            draw();
            return;
        }
        int mine = ++drawsAsked;
        drawIsWaiting = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            // a view asked for later wins
            if (mine != drawsAsked) return;
            drawIsWaiting = false;
            draw();
        });
    }

    /// <summary>
    /// The list was resized: the rows are made again when a different number of tiles fits across. While a pane beside
    /// it is being dragged, that waits until the drag pauses (see <see cref="ReflowCardsSoon"/>).
    /// </summary>
    private void CardsListResized(object sender, SizeChangedEventArgs e)
    {
        // with a division chosen, its own cards show instead
        if (loaded is null || view != UnitView.Cards || ShowsDivisionUnits || ShowsBattlegroup || drawIsWaiting) return;
        if (CardRowView.ColumnsFor(CardsRoom()) == cardColumns) return;
        // the first time the list has a width, the rows are made at once
        if (e.PreviousSize.Width == 0) ShowCards();
        else ReflowCardsSoon();
    }

    /// <summary>
    /// How long the list's width must stay put before its tiles are laid out again for a different number across.
    /// Making the rows again takes long when many small tiles show (at 50%, a column comes or goes every few dozen
    /// pixels), and doing it during a drag made the drag stutter; until then the tiles on screen stretch or narrow.
    /// </summary>
    private static readonly TimeSpan ReflowWait = TimeSpan.FromMilliseconds(150);

    /// <summary>Lays the tiles out again once the list's width has stayed put for <see cref="ReflowWait"/>.</summary>
    private System.Windows.Threading.DispatcherTimer? reflowTimer;

    /// <summary>Starts, or starts again, the wait before the tiles are laid out again for the list's width.</summary>
    private void ReflowCardsSoon()
    {
        if (reflowTimer is null)
        {
            reflowTimer = new System.Windows.Threading.DispatcherTimer { Interval = ReflowWait };
            reflowTimer.Tick += (_, _) =>
            {
                reflowTimer.Stop();
                ReflowCards();
            };
        }
        reflowTimer.Stop();
        reflowTimer.Start();
    }

    /// <summary>The tiles laid out again, the units' or a division's, when a different number now fits across.</summary>
    private void ReflowCards()
    {
        if (loaded is null || view != UnitView.Cards) return;
        if (ShowsBattlegroup || drawIsWaiting) return;
        if (!ShowsDivisionUnits && CardRowView.ColumnsFor(CardsRoom()) != cardColumns) ShowCards();
        if (ShowsDivisionUnits && CardRowView.ColumnsFor(DivisionCardsRoom()) != divisionCardColumns) ShowDivisionCards();
    }

    /// <summary>
    /// In the Cards view, Ctrl+Shift+[ folds every role's tiles away (a division's or a battlegroup's categories, with a
    /// division chosen) and Ctrl+Shift+] brings them all back, as on the Rules tab; not while typing in a box.
    /// </summary>
    private void FoldKeys(object sender, KeyEventArgs e)
    {
        if (loaded is null || part == Data.ArmoryPart.Weapons || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        if (ShowsBattlegroup)
        {
            FoldBattlegroupKeys(e);
            return;
        }
        if (view != UnitView.Cards) return;
        if (RulesPage.IsCollapseAllKey(e))
        {
            e.Handled = true;
            if (ShowsDivisionUnits) foldedCategories.UnionWith(shown.Select(unit => unit.Factory));
            else foldedRoles.UnionWith(shown.Select(unit => unit.Role));
            ShowCardsOfEither();
        }
        else if (RulesPage.IsExpandAllKey(e))
        {
            e.Handled = true;
            if (ShowsDivisionUnits) foldedCategories.Clear();
            else foldedRoles.Clear();
            ShowCardsOfEither();
        }
    }

    /// <summary>Ctrl+Shift+[ and Ctrl+Shift+] on the Battlegroups sub-tab: every category folds away, or comes back.</summary>
    private void FoldBattlegroupKeys(KeyEventArgs e)
    {
        if (RulesPage.IsCollapseAllKey(e))
        {
            e.Handled = true;
            foldedBattlegroupCategories.UnionWith(UnitNames.FactoriesOf(Game));
            DrawBattlegroup();
        }
        else if (RulesPage.IsExpandAllKey(e))
        {
            e.Handled = true;
            foldedBattlegroupCategories.Clear();
            DrawBattlegroup();
        }
    }

    /// <summary>The Cards view: a division's, by category, with a division chosen; else the units', by role.</summary>
    private void ShowCardsOfEither()
    {
        if (ShowsDivisionUnits) ShowDivisionCards();
        else ShowCards();
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

    /// <summary>
    /// The Table view: one row a unit, with the Units table's own columns; with a division chosen, also its cards, units
    /// per card and veterancy, changed in place.
    /// </summary>
    private void ShowTable()
    {
        IReadOnlyList<UnitTableColumn> columns = ShowsDivisionUnits ? DivisionTableColumns.For(chosenDivision!, KeepRule, UnitsAsGenerated()) : UnitTableColumn.All;
        TableList.Show(shown, columns, Game, selection, this, Texts.Get("Units.NoneMatch"));
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

    /// <summary>Whether a click is in a box of the table, where a division's cards, units per card and veterancy are changed.</summary>
    private static bool IsInBox(object? source)
    {
        for (DependencyObject? at = source as DependencyObject; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
            if (at is EditBox) return true;
        return false;
    }

    /// <summary>
    /// A click on a unit selects it, to show it in the Unit pane. Ctrl+Click adds it to the units selected, or takes it
    /// away; Shift+Click selects every unit from the one clicked last to this one.
    /// </summary>
    private void UnitListClicked(object sender, MouseButtonEventArgs e)
    {
        if (UnitAt(e.OriginalSource) is not Unit unit) return;
        // not in a box of the table, where the click is for typing
        if (!IsInBox(e.OriginalSource)) Focus();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) selection.Toggle(unit);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) selection.SelectRange(UnitsInOrder(), unit);
        else selection.Selected = unit;
    }

    /// <summary>
    /// The units the Cards or Table view shows, in the order they are on screen, for Shift+Click and Ctrl+A: a
    /// folded group's units are not among them.
    /// </summary>
    private IReadOnlyList<Unit> UnitsInOrder()
    {
        if (ShowsBattlegroup) return BattlegroupTilesInOrder();
        if (view == UnitView.Table) return TableList.UnitsInOrder;
        if (view != UnitView.Cards) return [];
        if (ShowsDivisionUnits) return DivisionTilesInOrder();
        return CardsList.ItemsSource is IEnumerable<CardLine> lines ? [.. lines.OfType<CardRowLine>().SelectMany(row => row.Units)] : [];
    }

    /// <summary>The units of the division's tiles, type by type, as they are on screen.</summary>
    private List<Unit> DivisionTilesInOrder()
    {
        var units = new List<Unit>();
        if (DivisionCardsContent.Content is not StackPanel groups) return units;
        // each type is its heading, then its tiles in a grid
        foreach (UniformGrid tiles in groups.Children.OfType<UniformGrid>())
            units.AddRange(tiles.Children.OfType<UnitTile>().Select(tile => tile.Unit));
        return units;
    }

    /// <summary>
    /// Ctrl+A in the Cards or Table view selects every unit shown; not while typing in a box, where it selects the
    /// box's text.
    /// </summary>
    private void SelectAllKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || Keyboard.Modifiers != ModifierKeys.Control || loaded is null) return;
        if ((view is not (UnitView.Cards or UnitView.Table) && !ShowsBattlegroup) || Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem) return;
        e.Handled = true;
        selection.SelectAll(UnitsInOrder());
    }

    /// <summary>
    /// A double-click on a unit shows it in the File Editor, when its file is on the Files tab; not in a box of the
    /// table, where a double-click picks a word.
    /// </summary>
    private void UnitListDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (IsInBox(e.OriginalSource)) return;
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
            // a right-click on one of several units selected keeps them all, for the menu to act on them all
            if (selection.IsSelected(unit) && selection.All.Count > 1)
            {
                OpenUnitsMenu(unit, [.. selection.All], (FrameworkElement)sender);
                return;
            }
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
        unitPaneDrawnFor = UnitPaneDrawnForNow();
        UnitPaneContent.Content = UnitPane.Build(selection.Selected, this);
        if (!ReferenceEquals(paneUnit, selection.Selected)) UnitPaneScroller.ScrollToTop();
        paneUnit = selection.Selected;
    }

    /// <summary>The Unit pane, unless it shows the unit selected as it would be drawn now (see UnitsPage.KeptViews.cs).</summary>
    private void ShowUnitPaneUnlessDrawn()
    {
        if (IsDrawnFor(unitPaneDrawnFor, UnitPaneDrawnForNow(), mayKeep: true))
        {
            UnitPane.ShowShowInUnits(UnitPaneContent.Content, ((IUnitActions)this).OffersShowInUnits);
            return;
        }
        ShowUnitPane();
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
        MainView.Of(this).AddUnits.Open(Texts.Get("Units.AddUnits.Title"), loaded.Catalog.Units, Game, compared.ToHashSet(), AddToCompare, vanilla: filter.VanillaUnits);
    }
}
