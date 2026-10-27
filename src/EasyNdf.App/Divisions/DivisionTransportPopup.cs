using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The pop-up for a unit's transports in one division, opened from the transport button on its tile: every transport
/// the game accepts for the unit, each with a tick (ticked, the unit has it in the division), grouped by kind, with a
/// search box, a nation filter, a choice of order (by name, cost or nation), Tick All and Untick All, and "No
/// transport needed". It is made for long lists: a freedom deck gives a
/// unit every transport it can have, dozens of them, so the list scrolls at a fixed height and each kind can be folded
/// away or ticked at once. Done or Enter keeps the changes, Esc or a click outside leaves them.
/// </summary>
public sealed class DivisionTransportPopup
{
    /// <summary>The pop-up's width, and the most its list grows to before it scrolls.</summary>
    private const double Width = 360, ListHeight = 330;

    /// <summary>The layer of the app's window the pop-up is drawn on, beside its button (see <see cref="FloatingPanel"/>).</summary>
    private readonly FloatingPanel floating;
    private readonly DivisionUnit rule;
    private readonly IDivisionEditing editing;
    private readonly Func<Unit, bool> isFirstSide;

    /// <summary>Every transport offered: the ones the game accepts, then any the unit has that it does not.</summary>
    private readonly List<Unit> offered;

    // what is being changed; kept only by Done or Enter
    private readonly HashSet<string> ticked;
    private bool noTransportNeeded;

    private readonly HashSet<TransportKinds.Kind> folded = [];
    private readonly TextBox search = new() { Margin = new Thickness(0, 0, 0, 5) };
    private readonly ComboBox nationChoice = new();
    private readonly ComboBox sortChoice = new() { Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel list = new();

    /// <summary>Opens the pop-up at the right of a tile's transport button.</summary>
    /// <param name="button">The button clicked.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="divisionName">The division's name, for the pop-up's second line.</param>
    /// <param name="rule">How the division has the unit now.</param>
    /// <param name="accepted">The transports the game accepts for the unit (see <see cref="Transports.ValidFor"/>).</param>
    /// <param name="units">The source's units by descriptor, for the transports the unit has that the game does not accept.</param>
    /// <param name="isFirstSide">Whether a unit is on the game's first side, for its nation's mark.</param>
    /// <param name="editing">What keeps the changes, in the open mod.</param>
    public static void Open(FrameworkElement button, Unit unit, string divisionName, DivisionUnit rule, IReadOnlyList<Unit> accepted,
        IReadOnlyDictionary<string, Unit> units, Func<Unit, bool> isFirstSide, IDivisionEditing editing) =>
        _ = new DivisionTransportPopup(button, unit, divisionName, rule, accepted, units, isFirstSide, editing);

    private DivisionTransportPopup(FrameworkElement button, Unit unit, string divisionName, DivisionUnit rule, IReadOnlyList<Unit> accepted,
        IReadOnlyDictionary<string, Unit> units, Func<Unit, bool> isFirstSide, IDivisionEditing editing)
    {
        this.rule = rule;
        this.editing = editing;
        this.isFirstSide = isFirstSide;
        // a transport the mod gave the unit that the game would not is still offered, so it can be taken away
        offered = [.. accepted, .. rule.Transports.Where(transport => accepted.All(each => each.Descriptor != transport)).Select(units.GetValueOrDefault).OfType<Unit>()];
        ticked = [.. rule.Transports];
        noTransportNeeded = rule.AvailableWithoutTransport;
        floating = MainView.Of(button).Floating;

        var parts = new StackPanel();
        parts.Children.Add(Title(unit.Name, divisionName));
        search.Tag = Texts.Get("Divisions.Transports.Popup.Search.Hint");
        search.ToolTip = Texts.Get("Divisions.Transports.Popup.Search.Tip");
        search.TextChanged += (_, _) => ShowList();
        parts.Children.Add(search);
        parts.Children.Add(Filters());
        parts.Children.Add(Tools());
        var scroller = new ScrollViewer { Content = list, MaxHeight = ListHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var well = new Bevel { Child = scroller };
        well.SetResourceReference(Bevel.TopLeftProperty, "lo");
        well.SetResourceReference(Bevel.BottomRightProperty, "hi");
        well.SetResourceReference(Bevel.BackgroundProperty, "well");
        parts.Children.Add(well);
        parts.Children.Add(Foot(unit));

        var box = new Bevel { Child = parts, Padding = new Thickness(9, 7, 9, 8), Width = Width, Focusable = true, FocusVisualStyle = null };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        box.PreviewKeyDown += KeyPressed;
        ShowList();
        floating.Show(box, button);
        // the keyboard goes to the search box, so typing finds a transport at once and Esc closes the pop-up
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => search.Focus());
    }

    /// <summary>"Transports" in bold, and the unit and division dim under it.</summary>
    private static StackPanel Title(string unitName, string divisionName)
    {
        var title = new TextBlock { Text = Texts.Get("Divisions.Transports.Popup.Title"), FontWeight = FontWeights.Bold };
        var sub = new TextBlock { Text = unitName + " · " + divisionName, FontSize = 11, Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return new StackPanel { Children = { title, sub } };
    }

    /// <summary>
    /// The nation filter (all nations, then those of the transports offered, A to Z) and the order, side by side, as
    /// the Add Units window has them.
    /// </summary>
    private Grid Filters()
    {
        StringComparer names = StringComparer.Create(Texts.Catalog.Culture, ignoreCase: true);
        nationChoice.Items.Add(new ComboBoxItem { Content = Texts.Get("Units.AddUnits.AllNations"), Tag = "" });
        foreach (string nation in offered.Select(transport => transport.Nation).Distinct().OrderBy(UnitNames.Nation, names))
            nationChoice.Items.Add(new ComboBoxItem { Content = UnitNames.Nation(nation), Tag = nation });
        nationChoice.SelectedIndex = 0;
        nationChoice.ToolTip = Texts.Get("Units.AddUnits.Nation.Tip");
        nationChoice.SelectionChanged += (_, _) => ShowList();
        UnitOrder.Fill(sortChoice);
        sortChoice.SelectionChanged += (_, _) => ShowList();
        var filters = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(sortChoice, 1);
        filters.Children.Add(nationChoice);
        filters.Children.Add(sortChoice);
        return filters;
    }

    /// <summary>How many are ticked, then Tick All and Untick All, which act on the transports the search shows.</summary>
    private DockPanel Tools()
    {
        summary.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var tickAll = new Button { Content = Texts.Get("Divisions.Transports.Popup.TickAll"), ToolTip = Texts.Get("Divisions.Transports.Popup.TickAll.Tip"), Margin = new Thickness(4, 0, 0, 0) };
        tickAll.Click += (_, _) => TickShown(Shown(), true);
        var untickAll = new Button { Content = Texts.Get("Divisions.Transports.Popup.UntickAll"), ToolTip = Texts.Get("Divisions.Transports.Popup.UntickAll.Tip"), Margin = new Thickness(4, 0, 0, 0) };
        untickAll.Click += (_, _) => TickShown(Shown(), false);
        DockPanel.SetDock(untickAll, Dock.Right);
        DockPanel.SetDock(tickAll, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 0, 0, 5), Children = { untickAll, tickAll, summary } };
    }

    /// <summary>"No transport needed", marked when the mod changed it, and Done.</summary>
    private DockPanel Foot(Unit unit)
    {
        var tick = new CheckBox { Content = Texts.Get("Divisions.Transports.NoneNeeded"), ToolTip = Texts.Get("Divisions.Transports.NoneNeeded.Tip"), IsChecked = noTransportNeeded, VerticalAlignment = VerticalAlignment.Center };
        tick.SetResourceReference(FrameworkElement.StyleProperty, "Tick");
        tick.Click += (_, _) => noTransportNeeded = tick.IsChecked == true;
        string unitName = unit.Descriptor;
        FrameworkElement markedTick = DivisionMarks.Mark(tick, DivisionMarks.UnitChanges(unitName, "NoTransport"),
            division => DivisionMarks.UnitIn(division, unitName) is DivisionUnit each ? (each.AvailableWithoutTransport ? "✓" : "–") : "");
        var done = new Button { Content = Texts.Get("Divisions.Veterancy.Popup.Done"), Margin = new Thickness(8, 0, 0, 0) };
        done.Click += (_, _) => Keep();
        DockPanel.SetDock(done, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false, Children = { done, markedTick } };
    }

    /// <summary>The transports the search and the nation filter let through, in the order chosen.</summary>
    private List<Unit> Shown()
    {
        string wanted = search.Text.Trim();
        string nation = (nationChoice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        return UnitOrder.Sort(offered.Where(transport =>
            (wanted == "" || transport.Name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase)) && (nation == "" || transport.Nation == nation)),
            UnitOrder.Chosen(sortChoice));
    }

    /// <summary>The list again: each kind's heading, then its transports unless it is folded away.</summary>
    private void ShowList()
    {
        list.Children.Clear();
        List<Unit> shown = Shown();
        foreach (IGrouping<TransportKinds.Kind, Unit> kind in shown.GroupBy(TransportKinds.KindOf).OrderBy(group => group.Key))
        {
            list.Children.Add(KindHeading(kind.Key, [.. kind]));
            if (folded.Contains(kind.Key)) continue;
            foreach (Unit transport in kind) list.Children.Add(Row(transport));
        }
        if (shown.Count == 0)
        {
            string empty = Texts.Get(offered.Count == 0 ? "Divisions.Transports.Popup.NoneAccepted" : "Divisions.Transports.Popup.NoneFound");
            var note = new TextBlock { Text = empty, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            list.Children.Add(note);
        }
        summary.Text = Texts.Get("Divisions.Transports.Popup.Ticked", ("ticked", Texts.Number(ticked.Count)), ("total", Texts.Number(offered.Count)));
    }

    /// <summary>
    /// A kind's heading: the fold button, its name in bold and how many of the kind are ticked. A click on the name
    /// ticks every transport of the kind shown, or unticks them all when all are ticked.
    /// </summary>
    private Bevel KindHeading(TransportKinds.Kind kind, List<Unit> shownOfKind)
    {
        bool isFolded = folded.Contains(kind);
        var fold = new Button
        {
            Content = isFolded ? "▸" : "▾", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 18,
            ToolTip = Texts.Get(isFolded ? "Divisions.Heading.Unfold.Tip" : "Divisions.Heading.Fold.Tip"),
        };
        fold.Click += (_, _) =>
        {
            if (!folded.Remove(kind)) folded.Add(kind);
            ShowList();
        };
        string heading = TransportKinds.Heading(kind);
        List<Unit> allOfKind = [.. offered.Where(transport => TransportKinds.KindOf(transport) == kind)];
        var name = new TextBlock { Text = heading, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        var count = new TextBlock
        {
            Text = Texts.Get("Divisions.Transports.Popup.Ticked", ("ticked", Texts.Number(allOfKind.Count(transport => ticked.Contains(transport.Descriptor)))), ("total", Texts.Number(allOfKind.Count))),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var words = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { name, count } };
        words.ToolTip = Texts.Get("Divisions.Transports.Group.Tip", ("kind", heading.ToLower(Texts.Catalog.Culture)));
        words.MouseLeftButtonUp += (_, _) => TickShown(shownOfKind, !shownOfKind.All(transport => ticked.Contains(transport.Descriptor)));
        var bar = new Bevel { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { fold, words } }, Padding = new Thickness(2, 2, 6, 2), Edges = new Thickness(0, 1, 0, 1) };
        bar.SetResourceReference(Bevel.TopLeftProperty, "hi");
        bar.SetResourceReference(Bevel.BottomRightProperty, "lo");
        bar.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return bar;
    }

    /// <summary>
    /// A transport's row: its tick, picture, name, nation's mark and cost. A click anywhere on it ticks or unticks it. (Its
    /// seats are not shown: WARNO counts a whole group as one seat, and every transport has one.)
    /// A transport the mod gave the unit or took away has "*" after its name, in the headings' colour, and the tooltip.
    /// </summary>
    private Bevel Row(Unit transport)
    {
        bool isTicked = ticked.Contains(transport.Descriptor);
        var tick = new CheckBox { IsChecked = isTicked, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        tick.SetResourceReference(FrameworkElement.StyleProperty, "Tick");
        var name = new TextBlock { Text = transport.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (!isTicked) name.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        // marked as the mod saved it, not as it is being ticked here
        if (DivisionMarks.TransportTipIfChanged(rule.Unit, transport.Descriptor, rule.Transports.Contains(transport.Descriptor)) is string tip)
        {
            name.ToolTip = tip;
            if (ModMarks.AreShown) name.Inlines.Add(ModMarks.Star());
        }
        // the cost as the Add Units window shows it
        var cost = new TextBlock { Text = transport.Cost is null ? "" : UnitNumbers.Of(transport.Cost), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Units.Stat.Cost") };
        cost.SetResourceReference(TextBlock.FontFamilyProperty, "mono");

        var cells = new Grid();
        foreach (GridLength width in new[] { new GridLength(18), new GridLength(40), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(38) })
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Place(cells, tick, 0, 0);
        Place(cells, new UnitPicture { Source = transport.PictureSource, Factory = transport.Factory, Width = 40 }, 1, 2);
        Place(cells, name, 2, 6);
        Place(cells, new NationMark(transport.Nation, isFirstSide(transport)), 3, 6);
        Place(cells, cost, 4, 6);

        var row = new Bevel { Child = cells, Padding = new Thickness(4, 2, 4, 2), Edges = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        // ticked rows on the hover colour, as the Add Units window's; the selection colour under the mouse
        void ShowState()
        {
            if (isTicked) row.SetResourceReference(Bevel.BackgroundProperty, "hover");
            else if (row.IsMouseOver) row.SetResourceReference(Bevel.BackgroundProperty, "sel");
            else row.Background = Brushes.Transparent;
        }
        row.MouseEnter += (_, _) => ShowState();
        row.MouseLeave += (_, _) => ShowState();
        row.MouseLeftButtonUp += (_, _) => TickShown([transport], !isTicked);
        ShowState();
        return row;
    }

    /// <summary>Puts a cell in its column, with a gap before it.</summary>
    private static void Place(Grid cells, FrameworkElement cell, int column, double gapBefore)
    {
        cell.Margin = new Thickness(gapBefore, 0, 0, 0);
        Grid.SetColumn(cell, column);
        cells.Children.Add(cell);
    }

    /// <summary>Ticks or unticks some transports, and shows the list again.</summary>
    private void TickShown(IEnumerable<Unit> transports, bool tick)
    {
        foreach (Unit transport in transports)
        {
            if (tick) ticked.Add(transport.Descriptor);
            else ticked.Remove(transport.Descriptor);
        }
        ShowList();
    }

    /// <summary>Esc leaves the changes; Enter keeps them.</summary>
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            floating.Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Keep();
        }
    }

    /// <summary>
    /// Done: the transports ticked and "No transport needed" kept in the mod, when they changed. The transports the unit
    /// had keep their order; those ticked here come after them, in the list's order.
    /// </summary>
    private void Keep()
    {
        floating.Close();
        List<string> transports = [.. rule.Transports.Where(ticked.Contains), .. offered.Select(transport => transport.Descriptor).Where(transport => ticked.Contains(transport) && !rule.Transports.Contains(transport))];
        if (transports.SequenceEqual(rule.Transports) && noTransportNeeded == rule.AvailableWithoutTransport) return;
        editing.ChangeRule(rule with { Transports = transports, AvailableWithoutTransport = noTransportNeeded });
    }
}
