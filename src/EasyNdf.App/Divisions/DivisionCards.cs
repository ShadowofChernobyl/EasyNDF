using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Armory's Cards view of a division: its units as tiles, under a heading for each type. The heading has a fold
/// button at its left, the only part that folds it (as a rule group's), the type's name, how many units, cards and
/// activation points it has, and at its right a button for each card's cost and a dashed + to add a card. Each tile
/// says how many cards the division has of the unit, and how many units in each.
/// </summary>
public static class DivisionCards
{
    /// <summary>The view for a division's groups, <paramref name="columns"/> tiles across.</summary>
    /// <param name="groups">The division's units by type, a type with no units shown included.</param>
    /// <param name="division">The division's name, for a type with no units.</param>
    /// <param name="inDivision">How many of the division's units are of a type, whether shown or not.</param>
    /// <param name="game">The game the units are from.</param>
    /// <param name="selection">Which unit is selected.</param>
    /// <param name="columns">How many tiles across.</param>
    /// <param name="folded">The types folded away, by code.</param>
    /// <param name="foldClicked">Folds a type away, or brings it back, when its fold button is clicked.</param>
    /// <param name="editing">What the cost buttons, Add Units and the tiles' pop-ups change, saved in the open mod.</param>
    /// <param name="units">The source's units by descriptor, for the tiles' pop-ups to tell the transports' kinds.</param>
    /// <param name="phase">The phase chosen above the cards, 0 to 2 for A to C, -1 for every phase.</param>
    /// <param name="phaseChosen">Chooses a phase, -1 for every phase, when one of the phase buttons is clicked.</param>
    public static StackPanel Build(IReadOnlyList<DivisionGroup> groups, string division, Func<string, int> inDivision, GameTitle game,
        UnitSelection selection, int columns, IReadOnlySet<string> folded, Action<string> foldClicked, IDivisionEditing editing, IReadOnlyDictionary<string, Unit> units,
        int phase, Action<int> phaseChosen)
    {
        var view = new StackPanel();
        string firstSide = UnitNames.SidesOf(game)[0];
        // a game whose cards count their units by phase (Steel Division 2) has the phases to choose above the cards
        if (groups.Any(group => group.Entries.Any(entry => entry.Rule.UnitsPerCardByPhase.Count > 0))) view.Children.Add(PhaseChoice(phase, phaseChosen));
        foreach (DivisionGroup group in groups)
        {
            bool isFolded = folded.Contains(group.Factory);
            view.Children.Add(Heading(group, isFolded, foldClicked, editing));
            if (isFolded) continue;
            if (group.Entries.Count == 0)
            {
                view.Children.Add(NoUnits(division, inDivision(group.Factory) > 0));
                continue;
            }
            var tiles = new UniformGrid { Columns = columns, Margin = new Thickness(CardRowView.Gap / 2) };
            foreach (DivisionEntry entry in group.Entries)
            {
                DivisionEntry shown = entry;
                // each tile is made when it comes into view (LazyTile); the grid's first now, which gives every cell its size
                var place = new LazyTile(entry.Unit, () => Tile(shown, division, units, firstSide, selection, editing, phase)) { Margin = new Thickness(CardRowView.Gap / 2) };
                if (tiles.Children.Count == 0) place.Make();
                tiles.Children.Add(place);
            }
            view.Children.Add(tiles);
        }
        return view;
    }

    /// <summary>A unit's tile in the division: its cards and units per card, its veterancy strip, its transports and its ×.</summary>
    private static UnitTile Tile(DivisionEntry entry, string division, IReadOnlyDictionary<string, Unit> units, string firstSide, UnitSelection selection,
        IDivisionEditing editing, int phase)
    {
        var tile = new UnitTile(entry.Unit, entry.Unit.Side == firstSide, selection);
        tile.ShowCards(Texts.Number(entry.Rule.Cards), DivisionNames.UnitsPerCard(entry.Rule),
            Texts.Count("Divisions.Card.Cards.Tip", entry.Rule.Cards),
            Texts.Get("Divisions.Card.UnitsPerCard.Tip", ("units", DivisionNames.UnitsPerCard(entry.Rule))));
        // Steel Division 2's: the units in a card in each phase, in the game's box, and the first phase's letter
        tile.ShowPhases(entry.Rule.UnitsPerCardByPhase, Texts.Get("Divisions.Card.UnitsPerCard.Tip", ("units", DivisionNames.UnitsPerCard(entry.Rule))), phase);
        MarkCardsIfChanged(tile, entry.Unit.Descriptor);
        Unit shownUnit = entry.Unit;
        tile.ShowTakeOut(Texts.Get("Divisions.Tile.Remove.Tip", ("unit", shownUnit.Name)), () => editing.RemoveUnit(shownUnit));
        // the game's veterancy marks beside the picture, where the division gives levels
        if (DivisionNames.UnitsAtEachVeterancy(entry.Rule) is { Count: > 0 } counts)
            tile.ShowVeterancy(counts, strip => DivisionUnitPopup.Open(strip, entry.Unit, division, entry.Rule, editing), DivisionNames.VeterancyTipsByPhase(entry.Rule));
        // under it, the transports' button, for a unit that can ride in one
        if (entry.Unit.SeatsTaken is not null)
            tile.ShowTransports(entry.Rule.Transports.Count > 0, TransportsTip(entry.Rule, units), button =>
                DivisionTransportPopup.Open(button, entry.Unit, division, entry.Rule, editing.TransportsFor(entry.Unit), units, unit => unit.Side == firstSide, editing));
        return tile;
    }

    /// <summary>
    /// Makes the tiles of a view made by <see cref="Build"/> that are in sight in its scroller, or about to be (a screen
    /// above and below), and leaves the others as empty places of the same size.
    /// </summary>
    /// <remarks>
    /// Tuned for speed: a big division (every unit of both sides, nearly 3,000) took minutes to show, its tiles all
    /// made at once, and held hundreds of megabytes; only those in sight are made now. Keep it that way when tidying.
    /// </remarks>
    public static void MakeTilesInView(StackPanel view, ScrollViewer scroller)
    {
        if (!scroller.IsVisible || scroller.ViewportHeight <= 0) return;
        double reach = scroller.ViewportHeight;
        var sight = new Rect(0, -reach, scroller.ActualWidth, scroller.ViewportHeight + 2 * reach);
        foreach (UniformGrid tiles in view.Children.OfType<UniformGrid>())
        {
            if (!tiles.IsVisible || !tiles.IsDescendantOf(scroller)) continue;
            Rect grid = tiles.TransformToAncestor(scroller).TransformBounds(new Rect(tiles.RenderSize));
            if (!grid.IntersectsWith(sight)) continue;
            foreach (LazyTile place in tiles.Children.OfType<LazyTile>())
            {
                if (place.IsMade) continue;
                Rect at = place.TransformToAncestor(scroller).TransformBounds(new Rect(place.RenderSize));
                if (at.IntersectsWith(sight)) place.Make();
            }
        }
    }

    /// <summary>The units of a view's tiles in a grid, in order, made or not.</summary>
    public static IEnumerable<Unit> UnitsIn(UniformGrid tiles) => tiles.Children.OfType<LazyTile>().Select(place => place.Unit);

    /// <summary>Changes how many tiles sit across a view made by <see cref="Build"/>, its tiles kept.</summary>
    public static void SetColumns(StackPanel view, int columns)
    {
        foreach (UniformGrid tiles in view.Children.OfType<UniformGrid>()) tiles.Columns = columns;
    }

    /// <summary>
    /// "Phase: All A B C" above the cards, joined buttons as the sides' are: a phase shows each card's units in that
    /// phase, and dims the cards that can't be taken in it; All shows every phase's.
    /// </summary>
    private static StackPanel PhaseChoice(int phase, Action<int> phaseChosen)
    {
        var label = new TextBlock { Text = Texts.Get("Divisions.Phase.Choice"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 4), Children = { label } };
        for (int choice = -1; choice <= 2; choice++)
        {
            int chosen = choice;
            var button = new ToggleButton
            {
                Content = choice < 0 ? Texts.Get("Divisions.Phase.All") : ((char)('A' + choice)).ToString(), IsChecked = phase == choice,
                Style = (Style)Application.Current.FindResource("PressedToggle"), Padding = new Thickness(9, 1, 9, 1), Margin = new Thickness(choice < 0 ? 0 : -1, 0, 0, 0),
                ToolTip = choice < 0 ? Texts.Get("Divisions.Phase.All.Tip") : Texts.Get("Divisions.Phase.One.Tip", ("phase", ((char)('A' + choice)).ToString())),
            };
            button.Click += (_, _) => phaseChosen(chosen);
            line.Children.Add(button);
        }
        return line;
    }

    /// <summary>The transport button's tooltip: how many transports of each kind the unit has, or that it has none.</summary>
    private static string TransportsTip(DivisionUnit rule, IReadOnlyDictionary<string, Unit> units)
    {
        string kinds = TransportKinds.Summary(rule.Transports.Select(units.GetValueOrDefault).OfType<Unit>());
        return kinds == "" ? Texts.Get("Divisions.Transports.Button.None.Tip") : Texts.Get("Divisions.Transports.Button.Tip", ("summary", kinds));
    }

    /// <summary>A tile's cards and units in each card marked when the open mod changed either (see <see cref="DivisionMarks"/>).</summary>
    private static void MarkCardsIfChanged(UnitTile tile, string unit)
    {
        string Detail(Division division) => DivisionMarks.UnitIn(division, unit) is DivisionUnit rule
            ? Texts.Get("Divisions.Card.Detail", ("cards", Texts.Number(rule.Cards)), ("units", DivisionNames.UnitsPerCard(rule)))
            : "";
        if (DivisionMarks.TipIfChanged(DivisionMarks.UnitChanges(unit, "Cards", "UnitsPerCard"), Detail) is string tip) tile.MarkCards(tip);
    }

    /// <summary>
    /// A type's heading: its name line, and under it, unless the type is folded, the line of its cost buttons. The
    /// heading is tagged with the type, so a change to the type's costs can redraw it alone (see
    /// <see cref="RedrawHeading"/>).
    /// </summary>
    private static StackPanel Heading(DivisionGroup group, bool isFolded, Action<string> foldClicked, IDivisionEditing editing)
    {
        var heading = new StackPanel { Tag = group.Factory, Children = { NameLine(group, isFolded, foldClicked, editing) } };
        // folded away, a type shows its name line alone: its card slots fold with its tiles
        if (!isFolded) heading.Children.Add(CostLine(group, editing));
        // a click on the heading itself is not a click on the list behind it, which would let go of the selected unit
        heading.MouseLeftButtonUp += (_, e) => e.Handled = true;
        return heading;
    }

    /// <summary>
    /// A type's name line, on the panel colour between lines: the fold button (▾, or ▸ when folded), its name in bold,
    /// its units, cards and points dim, and "+ Add Units…" at the right, for units of that type. A click elsewhere on
    /// it does nothing.
    /// </summary>
    private static Bevel NameLine(DivisionGroup group, bool isFolded, Action<string> foldClicked, IDivisionEditing editing)
    {
        var fold = new Button
        {
            Content = isFolded ? "▸" : "▾", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 20,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Texts.Get(isFolded ? "Divisions.Heading.Unfold.Tip" : "Divisions.Heading.Fold.Tip"),
        };
        string factory = group.Factory;
        System.Windows.Automation.AutomationProperties.SetName(fold, (string)fold.ToolTip);
        fold.Click += (_, _) => foldClicked(factory);
        var name = new TextBlock { Text = UnitNames.Factory(group.Factory), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var counts = new TextBlock { Text = Counts(group), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        counts.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var add = new Button
        {
            Content = Texts.Get("Divisions.Heading.AddUnits"), Style = (Style)Application.Current.FindResource("HeadingButton"),
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Divisions.Heading.AddUnits.Tip"),
        };
        add.Click += (_, _) => editing.AddUnits(factory);
        // off, with the reason, for a game whose divisions' units the Armory does not write yet, or a DLC division
        DivisionDetailsEditor.OffUnless(add, editing, IDivisionEditing.AddingUnits);
        DockPanel.SetDock(fold, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(add, Dock.Right);
        var line = new DockPanel { Children = { fold, name, add, counts } };
        var nameLine = new Bevel { Child = line, Padding = new Thickness(4, 3, 8, 3), Edges = new Thickness(0, 1, 0, 1) };
        nameLine.SetResourceReference(Bevel.TopLeftProperty, "hi");
        nameLine.SetResourceReference(Bevel.BottomRightProperty, "lo");
        nameLine.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return nameLine;
    }

    /// <summary>
    /// The line of a type's cost buttons under its name line, on the panel colour with a line under it, lined up with
    /// the type's name. It wraps, so every card shows however many there are.
    /// </summary>
    private static Bevel CostLine(DivisionGroup group, IDivisionEditing editing)
    {
        var costLine = new Bevel { Child = CostButtons.Build(group.Factory, group.Costs, editing), Padding = new Thickness(32, 4, 8, 4), Edges = new Thickness(0, 0, 0, 1) };
        costLine.SetResourceReference(Bevel.BottomRightProperty, "lo");
        costLine.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return costLine;
    }

    /// <summary>
    /// Draws one type's heading again in a view <see cref="Build"/> made, after a change to its costs, leaving the rest
    /// of the view (its tiles, and where it is scrolled to) as it was.
    /// </summary>
    public static void RedrawHeading(StackPanel view, DivisionGroup group, bool isFolded, Action<string> foldClicked, IDivisionEditing editing)
    {
        for (int at = 0; at < view.Children.Count; at++)
        {
            if (view.Children[at] is not StackPanel { Tag: string factory } || factory != group.Factory) continue;
            // taken out, then the new one put in its place: WPF refuses to swap a child at a place still taken
            view.Children.RemoveAt(at);
            view.Children.Insert(at, Heading(group, isFolded, foldClicked, editing));
            return;
        }
    }

    /// <summary>"6 units · 7 cards · 14 pts", or "0 units · no cards".</summary>
    private static string Counts(DivisionGroup group)
    {
        string units = Texts.Count("Divisions.Heading.Units", group.Entries.Count);
        if (group.Costs.Count == 0) return units + " · " + Texts.Get("Divisions.Heading.NoCards");
        return string.Join(" · ", units, Texts.Count("Divisions.Heading.Cards", group.Costs.Count), Texts.Count("Divisions.Heading.Points", group.Costs.Sum()));
    }

    /// <summary>
    /// Under a type with no units shown: that the division has none of that type (its heading's "+ Add Units…" adds
    /// some), or that the filters leave none of its units of that type.
    /// </summary>
    private static TextBlock NoUnits(string division, bool hiddenByFilters)
    {
        var text = new TextBlock
        {
            Text = hiddenByFilters ? Texts.Get("Divisions.Heading.NoneMatch") : Texts.Get("Divisions.Heading.NoUnits", ("division", division)),
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        text.Margin = new Thickness(12, 10, 12, 10);
        return text;
    }
}
