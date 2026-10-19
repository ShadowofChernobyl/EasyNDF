using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Text;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// The table of units, on the Units and Divisions tabs: one row a unit, sorted by the column whose heading was clicked,
/// with columns the user can widen or narrow by dragging their headings' edges. Each tab keeps its own column widths
/// (<see cref="WidthsKey"/>) and its own zoom (<see cref="Scale"/>).
/// </summary>
public sealed class UnitTableList : ItemsControl
{
    /// <summary>The room a vertical scrollbar takes, kept free so the columns do not move when it shows.</summary>
    private const double ScrollBarWidth = 14;

    private readonly UnitTableHeadings headings = new();
    private readonly TranslateTransform headingsOffset = new();

    // what the table shows, as last given to Show
    private IReadOnlyList<Unit> units = [];
    private IReadOnlyList<UnitTableColumn> columns = UnitTableColumn.All;
    private GameTitle game = GameTitle.Warno;
    private UnitSelection? selection;
    private IUnitActions? actions;
    private string emptyText = "";

    /// <summary>The sort: the column's key, and which way.</summary>
    private string sortKey = "Cost";
    private bool sortAscending;

    /// <summary>The columns' widths when the table was last filled.</summary>
    private double[] widths = [];

    /// <summary>The units' descriptors in the order last shown, to tell a refill of the same rows from new ones.</summary>
    private List<string> shownOrder = [];

    /// <summary>The table's scrolling frame, from its template.</summary>
    private ScrollViewer? scroller;

    /// <summary>Builds an empty table in the shared look; <see cref="Show"/> fills it.</summary>
    public UnitTableList()
    {
        Style = (Style)Application.Current.FindResource("UnitTableList");
        headings.RenderTransform = headingsOffset;
        headings.SortClicked += SortBy;
        headings.ResizeStarted += KeepUnitColumnWidth;
        headings.ColumnResized += ResizeColumn;
        headings.ResizeEnded += () => { AppData.Current.SaveState(); Fill(); };
        headings.ColumnReset += key =>
        {
            (AppData.Current.State[WidthsKey] as JsonObject)?.Remove(key);
            AppData.Current.SaveState();
            Fill();
        };
        SizeChanged += Resized;
    }

    /// <summary>The name the columns' dragged widths are kept under between sessions: each tab has its own.</summary>
    public string WidthsKey { get; set; } = "unitColumns";

    /// <summary>How large the rows and headings are drawn: 1 at 100% zoom.</summary>
    public double Scale { get; private set; } = 1;

    /// <summary>The headings sit in the table's scrolling frame, above the rows, and move sideways with them.</summary>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (Template.FindName("TableScroller", this) is not ScrollViewer found) return;
        ScrollViewer scroller = found;
        this.scroller = scroller;
        scroller.Tag = headings;
        scroller.ApplyTemplate();
        if (scroller.Template.FindName("HeadingsClip", scroller) is Border headingsClip)
            headingsClip.SizeChanged += (_, _) => headingsClip.Clip = new RectangleGeometry(new Rect(0, -1, headingsClip.ActualWidth, headingsClip.ActualHeight + 2));
        // the offset is inside the headings' zoom, so it is made smaller by it to move as far as the rows on screen
        scroller.ScrollChanged += (_, _) => headingsOffset.X = -scroller.HorizontalOffset / Scale;
    }

    /// <summary>Shows units in the table, sorted as it was; <paramref name="emptyText"/> when there are none.</summary>
    /// <param name="units">The units, in the order the tab lists them.</param>
    /// <param name="columns">The columns: <see cref="UnitTableColumn.All"/>, or those with columns of a division's added.</param>
    /// <param name="game">The game the units are from, for the headings.</param>
    /// <param name="selection">Which unit is selected; the rows follow it.</param>
    /// <param name="actions">The tab the table is on.</param>
    /// <param name="emptyText">The line shown when there are no units.</param>
    public void Show(IReadOnlyList<Unit> units, IReadOnlyList<UnitTableColumn> columns, GameTitle game, UnitSelection selection, IUnitActions actions, string emptyText)
    {
        this.units = units;
        this.columns = columns;
        this.game = game;
        this.selection = selection;
        this.actions = actions;
        this.emptyText = emptyText;
        // a sort by a column these columns do not have goes back to the cost
        if (!columns.Any(column => column.Key == sortKey))
        {
            sortKey = "Cost";
            sortAscending = false;
        }
        Fill();
    }

    /// <summary>
    /// Draws the rows and headings at a zoom. A transform in a style cannot change once the style is in use, so each
    /// zoom gets a new style.
    /// </summary>
    public void SetScale(double scale)
    {
        Scale = scale;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        ItemContainerStyle = new Style(typeof(ContentPresenter)) { Setters = { new Setter(LayoutTransformProperty, transform) } };
        headings.LayoutTransform = transform;
    }

    /// <summary>
    /// Fills the table: one row a unit, sorted by the column chosen. Each number's bar is filled as far as the biggest
    /// number of its column among the units listed.
    /// </summary>
    /// <param name="keepHeadings">While a column's edge is dragged: the headings only change their widths.</param>
    public void Fill(bool keepHeadings = false)
    {
        if (selection is null || actions is null) return;
        widths = UnitTableColumn.Widths(columns, Room(), ChosenWidths());
        if (keepHeadings) headings.SetWidths(widths);
        else headings.Show(game, columns, widths, sortKey, sortAscending);
        UnitTableColumn sortColumn = columns.First(column => column.Key == sortKey);
        List<Unit> sorted = sortColumn.Sort(units, sortAscending);
        var most = new Dictionary<string, double>();
        foreach (UnitTableColumn column in columns.Where(column => column.IsNumber))
            most[column.Key] = sorted.Select(column.Number!).Where(value => value > 0).Select(value => value!.Value).DefaultIfEmpty(0).Max();
        List<object> lines = [.. sorted.Select(unit => new TableRowLine(unit, columns, widths, most, selection, actions))];
        if (lines.Count == 0) lines.Add(new EmptyLine(emptyText));
        // the same rows again, as after a value changed in the table: the table stays where it was scrolled to
        List<string> order = [.. sorted.Select(unit => unit.Descriptor)];
        bool sameRows = order.SequenceEqual(shownOrder);
        shownOrder = order;
        (double down, double across) = (scroller?.VerticalOffset ?? 0, scroller?.HorizontalOffset ?? 0);
        ItemsSource = lines;
        if (sameRows && scroller is not null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                scroller.ScrollToVerticalOffset(down);
                scroller.ScrollToHorizontalOffset(across);
            });
    }

    /// <summary>The width the columns have across, beside the scrollbar, in the rows' own units: less when zoomed in.</summary>
    private double Room() => (ActualWidth - ScrollBarWidth) / Scale;

    /// <summary>The table was resized: the columns are laid out again when the Unit column's width changes.</summary>
    private void Resized(object sender, SizeChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible || selection is null) return;
        double[] fitted = UnitTableColumn.Widths(columns, Room(), ChosenWidths());
        if (widths.Length == 0 || Math.Abs(fitted[0] - widths[0]) >= 1) Fill();
    }

    /// <summary>The columns' widths as the user dragged them, by key, kept between sessions.</summary>
    private Dictionary<string, double> ChosenWidths() =>
        AppData.Current.State[WidthsKey] is JsonObject chosen
            ? chosen.Where(width => width.Value is JsonValue).ToDictionary(width => width.Key, width => width.Value!.GetValue<double>())
            : [];

    /// <summary>The kept widths, made when the first is kept.</summary>
    private JsonObject KeptWidths()
    {
        if (AppData.Current.State[WidthsKey] is not JsonObject kept) AppData.Current.State[WidthsKey] = kept = [];
        return kept;
    }

    /// <summary>
    /// A drag starts: the Unit column, which fills the room until now, keeps the width it has, so widening another
    /// column moves the columns after it rather than shrinking the Unit column. A double-click on its edge lets it fill again.
    /// </summary>
    private void KeepUnitColumnWidth()
    {
        JsonObject kept = KeptWidths();
        string unitKey = columns[0].Key;
        if (!kept.ContainsKey(unitKey) && widths.Length > 0) kept[unitKey] = Math.Round(columns[0].Fit(widths[0]));
    }

    /// <summary>A column's edge dragged: the column takes the width, within its limits, and the rows follow at once.</summary>
    private void ResizeColumn(string key, double width)
    {
        KeptWidths()[key] = Math.Round(columns.First(column => column.Key == key).Fit(width));
        Fill(keepHeadings: true);
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
            sortAscending = !columns.First(column => column.Key == key).IsNumber;
        }
        Fill();
    }
}
