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

    /// <summary>The Table view's sort: the column's key, and which way.</summary>
    private string sortKey = "Cost";
    private bool sortAscending;

    /// <summary>The Table view's column widths when it was last filled.</summary>
    private double[] tableWidths = [];

    private readonly UnitTableHeadings tableHeadings = new();

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
        cardColumns = CardRowView.ColumnsFor(CardsList.ActualWidth - ScrollBarWidth);
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

    /// <summary>The list was resized: the rows are made again when a different number of tiles fits across.</summary>
    private void CardsListResized(object sender, SizeChangedEventArgs e)
    {
        if (loaded is null || view != UnitView.Cards) return;
        if (CardRowView.ColumnsFor(CardsList.ActualWidth - ScrollBarWidth) != cardColumns) ShowCards();
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

    /// <summary>The headings sit in the table's scrolling frame, above the rows, and move sideways with them.</summary>
    private void PrepareTableHeadings()
    {
        TableList.ApplyTemplate();
        var scroller = (ScrollViewer)TableList.Template.FindName("TableScroller", TableList);
        scroller.Tag = tableHeadings;
        scroller.ApplyTemplate();
        if (scroller.Template.FindName("HeadingsClip", scroller) is Border headingsClip)
            headingsClip.SizeChanged += (_, _) => headingsClip.Clip = new RectangleGeometry(new Rect(0, -1, headingsClip.ActualWidth, headingsClip.ActualHeight + 2));
        var headingsOffset = new TranslateTransform();
        tableHeadings.RenderTransform = headingsOffset;
        scroller.ScrollChanged += (_, _) => headingsOffset.X = -scroller.HorizontalOffset;
        tableHeadings.SortClicked += SortBy;
        tableHeadings.ColumnResized += ResizeColumn;
        tableHeadings.ResizeEnded += () => { AppData.Current.SaveState(); ShowTable(); };
        tableHeadings.ColumnReset += key =>
        {
            (AppData.Current.State["unitColumns"] as JsonObject)?.Remove(key);
            AppData.Current.SaveState();
            ShowTable();
        };
    }

    /// <summary>The columns' widths as the user dragged them, by key, kept between sessions.</summary>
    private static Dictionary<string, double> ChosenColumnWidths() =>
        AppData.Current.State["unitColumns"] is JsonObject widths
            ? widths.Where(width => width.Value is JsonValue).ToDictionary(width => width.Key, width => width.Value!.GetValue<double>())
            : [];

    /// <summary>A column's edge dragged: the column takes the width, within its limits, and the rows follow at once.</summary>
    private void ResizeColumn(string key, double width)
    {
        if (AppData.Current.State["unitColumns"] is not JsonObject widths) AppData.Current.State["unitColumns"] = widths = [];
        widths[key] = Math.Round(UnitTableColumn.All.First(column => column.Key == key).Fit(width));
        ShowTable(keepHeadings: true);
    }

    /// <summary>
    /// The Table view: one row a unit, sorted by the column chosen. Each number's bar is filled as far as the biggest
    /// number of its column among the units listed.
    /// </summary>
    /// <param name="keepHeadings">While a column's edge is dragged: the headings only change their widths.</param>
    private void ShowTable(bool keepHeadings = false)
    {
        tableWidths = UnitTableColumn.Widths(TableList.ActualWidth - ScrollBarWidth, ChosenColumnWidths());
        if (keepHeadings) tableHeadings.SetWidths(tableWidths);
        else tableHeadings.Show(Game, tableWidths, sortKey, sortAscending);
        UnitTableColumn sortColumn = UnitTableColumn.All.First(column => column.Key == sortKey);
        List<Unit> sorted = sortColumn.Sort(shown, sortAscending);
        var most = new Dictionary<string, double>();
        foreach (UnitTableColumn column in UnitTableColumn.All.Where(column => column.IsNumber))
            most[column.Key] = sorted.Select(column.Number!).Where(value => value > 0).Select(value => value!.Value).DefaultIfEmpty(0).Max();
        List<object> lines = [.. sorted.Select(unit => new TableRowLine(unit, tableWidths, most, selection, this))];
        if (lines.Count == 0) lines.Add(new EmptyLine(Texts.Get("Units.NoneMatch")));
        TableList.ItemsSource = lines;
    }

    /// <summary>The table was resized: the columns are laid out again when the Unit column's width changes.</summary>
    private void TableListResized(object sender, SizeChangedEventArgs e)
    {
        if (loaded is null || view != UnitView.Table) return;
        double[] widths = UnitTableColumn.Widths(TableList.ActualWidth - ScrollBarWidth, ChosenColumnWidths());
        if (tableWidths.Length == 0 || Math.Abs(widths[0] - tableWidths[0]) >= 1) ShowTable();
    }

    /// <summary>
    /// A heading clicked: the table is sorted by its column; clicked again, the other way. Words sort A to Z first,
    /// numbers biggest first.
    /// </summary>
    private void SortBy(string key)
    {
        if (key == sortKey) sortAscending = !sortAscending;
        else
        {
            sortKey = key;
            sortAscending = !UnitTableColumn.All.First(column => column.Key == key).IsNumber;
        }
        ShowTable();
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
        AppData.Current.State["unitView"] = view.ToString();
        AppData.Current.SaveState();
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
