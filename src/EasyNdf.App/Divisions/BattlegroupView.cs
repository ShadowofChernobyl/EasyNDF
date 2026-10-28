using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// A division's Official Battlegroup, as the game's Battlegroups list shows it: its activation points at the top, then
/// its cards in rows, one a type, in the order the game shows them. Each card is a unit's tile with the units in it, its
/// veterancy and its transport. What would stop GenerateMod (see <see cref="BattlegroupCheck"/>) is listed above the
/// rows, and the cards it is about are outlined. The cards are changed in place: dragged within their row to change
/// the order, taken out with the × on their corner (or their menu), and added with the + at the row's end. Undo takes
/// any of these back, so none asks first.
/// </summary>
public static class BattlegroupView
{
    /// <summary>The view of a division's battlegroup.</summary>
    /// <param name="division">The division, as the open mod has it, with its battlegroup.</param>
    /// <param name="units">The source's units by descriptor.</param>
    /// <param name="game">The game the units are from.</param>
    /// <param name="selection">Which unit is selected: a click on a card selects its unit.</param>
    /// <param name="columns">How many tiles across, as in a division's Cards view.</param>
    /// <param name="editing">What changes the battlegroup, saved in the open mod.</param>
    /// <param name="addClicked">What a row's + does, given the + and the row's type: Add a Card.</param>
    /// <param name="folded">The types folded away, by code.</param>
    /// <param name="foldClicked">Folds a type away, or brings it back, when its fold button is clicked.</param>
    /// <param name="hasPlace">
    /// Whether the division has a battlegroup to show and change: one of the game's, or the mod's new division's, which
    /// gets one with its first card. A game division the source gives none cannot get one (no rule could write it).
    /// </param>
    /// <param name="tried">The places of the cards Generation Settings being tried add, outlined in the accent colour.</param>
    /// <param name="whyChosen">Why a generated card was chosen, for its tooltip: "anti-air · good value".</param>
    public static FrameworkElement Build(Division division, IReadOnlyDictionary<string, Unit> units, GameTitle game, UnitSelection selection, int columns,
        IBattlegroupEditing editing, Action<FrameworkElement, string> addClicked, IReadOnlySet<string> folded, Action<string> foldClicked, bool hasPlace,
        IReadOnlySet<int>? tried = null, Func<GeneratedCard, string>? whyChosen = null)
    {
        // Steel Division 2's decks are laid out differently, and are not read
        if (game != GameTitle.Warno) return Muted(Texts.Get("Divisions.Battlegroup.NotForThisGame", ("game", game.Name)));
        if (!hasPlace) return Muted(Texts.Get("Divisions.Battlegroup.None", ("division", division.Name)));
        IReadOnlyList<BattlegroupCard> cards = division.Battlegroup?.Cards ?? [];

        string? TypeOf(string unit) => units.TryGetValue(unit, out Unit? found) ? found.Factory : null;
        List<BattlegroupProblem> problems = BattlegroupCheck.Problems(cards, division, TypeOf);
        HashSet<int> outlined = [.. problems.SelectMany(problem => problem.Cards)];
        var view = new StackPanel();
        view.Children.Add(PointsLine(BattlegroupCheck.Points(cards, division, TypeOf), division.ActivationPoints, cards.Count));
        if (cards.Count == 0) view.Children.Add(Muted(Texts.Get("Divisions.Battlegroup.Empty")));
        if (problems.Count > 0) view.Children.Add(ProblemsBox(problems, units));
        string firstSide = UnitNames.SidesOf(game)[0];
        foreach (string type in UnitNames.FactoriesOf(game))
        {
            List<int> places = [.. Enumerable.Range(0, cards.Count).Where(place => TypeOf(cards[place].Unit) == type)];
            int room = division.Costs.TryGetValue(type, out IReadOnlyList<int>? costs) ? costs.Count : 0;
            // a type the division has no room for, and no card of, has no row
            if (places.Count == 0 && room == 0) continue;
            bool isFolded = folded.Contains(type);
            view.Children.Add(TypeHeading(type, places.Count, room, isFolded, foldClicked));
            if (isFolded) continue;
            var tiles = new UniformGrid { Columns = columns, Margin = new Thickness(CardRowView.Gap / 2) };
            foreach (int place in places)
            {
                string? why = division.Battlegroup?.Generated is { } generated && place < generated.Count ? whyChosen?.Invoke(generated[place]) : null;
                tiles.Children.Add(Card(cards[place], place, type, division, units, firstSide, selection, outlined.Contains(place), tried?.Contains(place) == true, why, editing));
            }
            // the + after the row's last card; dropped on, a card goes to the row's end
            int end = places.Count > 0 ? places[^1] + 1 : cards.Count;
            tiles.Children.Add(AddTile(type, end, addClicked, editing));
            view.Children.Add(tiles);
        }
        return view;
    }

    /// <summary>
    /// The line at the top: "Activation Points 31/50", the points in the warning colour when they are more than the
    /// division has, and how many cards at the right.
    /// </summary>
    private static Bevel PointsLine(int points, int? allowed, int cards)
    {
        var label = new TextBlock { Text = Texts.Get("Divisions.Battlegroup.Points"), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var value = new TextBlock
        {
            Text = Texts.Number(points) + "/" + UnitNumbers.Of(allowed), FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        if (allowed is int most && points > most) value.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        var count = new TextBlock { Text = Texts.Count("Divisions.Battlegroup.CardCount", cards), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(count, Dock.Right);
        DockPanel.SetDock(label, Dock.Left);
        var line = new Bevel { Child = new DockPanel { Children = { count, label, value } }, Padding = new Thickness(10, 6, 10, 6), Edges = new Thickness(0, 0, 0, 1) };
        line.SetResourceReference(Bevel.BottomRightProperty, "line");
        return line;
    }

    /// <summary>What would stop GenerateMod, a line each, in a box edged in the warning colour.</summary>
    private static Border ProblemsBox(IEnumerable<BattlegroupProblem> problems, IReadOnlyDictionary<string, Unit> units)
    {
        var lines = new StackPanel();
        // cards of units the source does not have, said once for all of them: what to do is the same for each
        List<string> unknown = [.. problems.Where(problem => IsUnknownUnit(problem, units)).Select(problem => problem.Subject).Distinct()];
        if (unknown.Count > 1) lines.Children.Add(ProblemLine(UnknownUnitsText(unknown)));
        foreach (BattlegroupProblem problem in problems.Where(problem => unknown.Count <= 1 || !IsUnknownUnit(problem, units)))
            lines.Children.Add(ProblemLine(ProblemText(problem, units)));
        var box = new Border { Child = lines, BorderThickness = new Thickness(1), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(10, 8, 10, 4) };
        box.SetResourceReference(Border.BorderBrushProperty, "warn");
        return box;
    }

    /// <summary>Several cards of units the source does not have, in one line: how many, the first five, and what to do.</summary>
    public static string UnknownUnitsText(IReadOnlyList<string> units) => Texts.Get("Divisions.Battlegroup.Problem.UnitsUnknown",
        ("count", units.Count), ("units", string.Join(", ", units.Take(5).Select(ShortName)) + (units.Count > 5 ? "…" : "")));

    /// <summary>A line of the problems box, with the warning sign.</summary>
    private static TextBlock ProblemLine(string text)
    {
        var line = new TextBlock { Text = "⚠ " + text, TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        return line;
    }

    /// <summary>
    /// Whether a problem is a card of a unit the source does not have at all. A mod adds such a unit with its rules on
    /// UniteDescriptor.ndf, which the Armory runs only when that file is on the Files tab.
    /// </summary>
    private static bool IsUnknownUnit(BattlegroupProblem problem, IReadOnlyDictionary<string, Unit> units) =>
        problem.Kind == BattlegroupProblemKind.UnitNotInDivision && !units.ContainsKey(problem.Subject);

    /// <summary>A unit's descriptor without "Descriptor_Unit_": "YS_Doomsday".</summary>
    public static string ShortName(string descriptor) =>
        descriptor.StartsWith("Descriptor_Unit_", StringComparison.Ordinal) ? descriptor["Descriptor_Unit_".Length..] : descriptor;

    /// <summary>One problem in words: "M1A1 ABRAMS: 7 cards, the division allows 6."</summary>
    public static string ProblemText(BattlegroupProblem problem, IReadOnlyDictionary<string, Unit> units)
    {
        string unit = units.TryGetValue(problem.Subject, out Unit? found) ? found.Name : problem.Subject;
        string transport = problem.Transport is string each && units.TryGetValue(each, out Unit? ride) ? ride.Name : problem.Transport ?? "";
        return problem.Kind switch
        {
            BattlegroupProblemKind.UnitNotInDivision when found is null => Texts.Get("Divisions.Battlegroup.Problem.UnitUnknown", ("unit", ShortName(unit))),
            BattlegroupProblemKind.UnitNotInDivision => Texts.Get("Divisions.Battlegroup.Problem.UnitNotInDivision", ("unit", unit)),
            BattlegroupProblemKind.NeedsTransport => Texts.Get("Divisions.Battlegroup.Problem.NeedsTransport", ("unit", unit)),
            BattlegroupProblemKind.TransportNotAllowed => Texts.Get("Divisions.Battlegroup.Problem.TransportNotAllowed", ("unit", unit), ("transport", transport)),
            BattlegroupProblemKind.VeterancyNotOffered => Texts.Get("Divisions.Battlegroup.Problem.VeterancyNotOffered", ("unit", unit), ("level", problem.Count)),
            BattlegroupProblemKind.TooManyCardsOfUnit => Texts.Get("Divisions.Battlegroup.Problem.TooManyCardsOfUnit", ("unit", unit), ("count", problem.Count), ("allowed", problem.Allowed)),
            BattlegroupProblemKind.TooManyCardsOfType => Texts.Get("Divisions.Battlegroup.Problem.TooManyCardsOfType",
                ("type", UnitNames.Factory(problem.Subject)), ("count", problem.Count), ("allowed", problem.Allowed)),
            _ => Texts.Get("Divisions.Battlegroup.Problem.TooManyPoints", ("count", problem.Count), ("allowed", problem.Allowed)),
        };
    }

    /// <summary>
    /// A type's heading, as in a division's Cards view: the fold button (▾, or ▸ when folded), its name in bold, and
    /// how many cards it has of how many the division has room for, dim; in the caution colour when full, the warning
    /// colour when over.
    /// </summary>
    private static Bevel TypeHeading(string type, int cards, int room, bool isFolded, Action<string> foldClicked)
    {
        var fold = new Button
        {
            Content = isFolded ? "▸" : "▾", Style = (Style)Application.Current.FindResource("HeadingButton"), Padding = new Thickness(0), Width = 20,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Texts.Get(isFolded ? "Divisions.Battlegroup.Unfold.Tip" : "Divisions.Heading.Fold.Tip"),
        };
        fold.Click += (_, _) => foldClicked(type);
        var name = new TextBlock { Text = UnitNames.Factory(type), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock
        {
            Text = Texts.Number(cards) + "/" + Texts.Number(room), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Texts.Get("Divisions.Battlegroup.TypeCount.Tip"),
        };
        count.SetResourceReference(TextBlock.ForegroundProperty, cards > room ? "warn" : cards == room ? "caution" : "muted");
        var heading = new Bevel { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { fold, name, count } }, Padding = new Thickness(4, 3, 8, 3), Edges = new Thickness(0, 1, 0, 1) };
        heading.SetResourceReference(Bevel.TopLeftProperty, "hi");
        heading.SetResourceReference(Bevel.BottomRightProperty, "lo");
        heading.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return heading;
    }

    /// <summary>
    /// One card: its unit's tile with the units in the card (as the division gives them at its veterancy), its
    /// veterancy and its transport; outlined in the warning colour when a problem is about it. A unit the source does
    /// not have shows as its descriptor.
    /// </summary>
    private static FrameworkElement Card(BattlegroupCard card, int place, string type, Division division, IReadOnlyDictionary<string, Unit> units,
        string firstSide, UnitSelection selection, bool outlined, bool isTried, string? why, IBattlegroupEditing editing)
    {
        if (!units.TryGetValue(card.Unit, out Unit? unit)) return Muted(card.Unit);
        var tile = new UnitTile(unit, unit.Side == firstSide, selection);
        string? transport = card.Transport is null ? null : units.TryGetValue(card.Transport, out Unit? ride) ? ride.Name : card.Transport;
        string tip = transport is null
            ? Texts.Get("Divisions.Battlegroup.Card.Tip", ("unit", unit.Name), ("level", card.Veterancy))
            : Texts.Get("Divisions.Battlegroup.CardInTransport.Tip", ("unit", unit.Name), ("level", card.Veterancy), ("transport", transport));
        // a generated card says why it was chosen; it can't be dragged, as its place is chosen again at each export
        string more = why is null ? Texts.Get("Divisions.Battlegroup.Drag.Tip") : Texts.Get("Divisions.Battlegroup.Generation.Chosen.Tip", ("why", why));
        // the game's own cards all have Number 1; another (a mod's, such as YSM's 999) is kept, and said
        if (card.Number != 1) tip += "\n" + Texts.Get("Divisions.Battlegroup.CardNumber.Tip", ("number", card.Number));
        tile.ShowBattlegroupCard(UnitsInCard(card, division), transport, tip + "\n" + more);
        // its veterancy and transport, chosen on the strip and the button at the picture's right, as on a division's tile
        if (division.Units.FirstOrDefault(each => each.Unit == card.Unit) is DivisionUnit rule)
        {
            if (DivisionNames.UnitsAtEachVeterancy(rule) is { Count: > 0 } counts)
                tile.ShowVeterancyChoice(counts, card.Veterancy, level => editing.ChangeCard(place, card with { Veterancy = level }));
            if (rule.Transports.Count > 0)
                tile.ShowTransports(card.Transport is not null, TransportTip(transport), button => TransportMenu(button, card, place, rule, units, editing));
        }
        tile.ShowTakeOut(Texts.Get("Divisions.Battlegroup.TakeOut.Tip"), () => editing.TakeOutCard(place));
        var holder = new Grid { Children = { tile, DropMark() } };
        var outline = new Border { Child = holder, Margin = new Thickness(CardRowView.Gap / 2 - 1), BorderThickness = new Thickness(1), Tag = new CardPlace(place, type) };
        if (outlined) outline.SetResourceReference(Border.BorderBrushProperty, "warn");
        else if (isTried) outline.SetResourceReference(Border.BorderBrushProperty, "accent");
        outline.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            selection.Selected = unit;
            Menus.Show(outline,
            [
                new MenuChoice(Texts.Get("Divisions.Battlegroup.TakeOut"), () => editing.TakeOutCard(place), Tip: Texts.Get("Divisions.Battlegroup.TakeOut.Tip")),
                Menus.Line,
                new MenuChoice(Texts.Get("Divisions.Battlegroup.MoveEarlier"), () => editing.MoveCard(place, place - 1), IsEnabled: place > 0),
                new MenuChoice(Texts.Get("Divisions.Battlegroup.MoveLater"), () => editing.MoveCard(place, place + 1), IsEnabled: place < division.Battlegroup!.Cards.Count - 1),
            ]);
        };
        MakeDraggable(outline, tile, place, type, editing);
        return outline;
    }

    /// <summary>The transport button's tooltip: the card's transport, or that it has none.</summary>
    private static string TransportTip(string? transport) => transport is null
        ? Texts.Get("Divisions.Battlegroup.Transport.None.Tip")
        : Texts.Get("Divisions.Battlegroup.Transport.Tip", ("transport", transport));

    /// <summary>
    /// The card's transports, under its transport button: "No transport" when the division lets the unit go without
    /// one, then each transport the division gives it; the card's own ticked. A choice gives the card that transport.
    /// </summary>
    private static void TransportMenu(FrameworkElement button, BattlegroupCard card, int place, DivisionUnit rule, IReadOnlyDictionary<string, Unit> units,
        IBattlegroupEditing editing)
    {
        var choices = new List<MenuChoice?>();
        MenuChoice Choice(string label, string? transport) => new(label, () =>
        {
            if (transport != card.Transport) editing.ChangeCard(place, card with { Transport = transport });
        }, Symbol: transport == card.Transport ? "✓" : "");
        if (rule.AvailableWithoutTransport) choices.Add(Choice(Texts.Get("Divisions.Battlegroup.AddCard.NoTransport"), null));
        foreach (string transport in rule.Transports)
            choices.Add(Choice(units.TryGetValue(transport, out Unit? ride) ? ride.Name : transport, transport));
        Menus.ShowBelow(button, choices);
    }

    /// <summary>Where a card is in the battlegroup, and its row's type: what a drag carries, and what a drop is onto.</summary>
    private sealed record CardPlace(int Place, string Type);

    /// <summary>The bar at a card's left that shows a card dropped there goes before it; hidden until a card is dragged over.</summary>
    private static Rectangle DropMark()
    {
        var mark = new Rectangle { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-5, 0, 0, 0), Visibility = Visibility.Collapsed };
        mark.SetResourceReference(Shape.FillProperty, "accent");
        return mark;
    }

    /// <summary>
    /// A card dragged within its row: pressed and moved a little, it is carried; over another card of the row, a bar
    /// shows it goes before that one; dropped, it is moved there. A card cannot leave its row, as its type is the row's.
    /// </summary>
    private static void MakeDraggable(Border outline, UnitTile tile, int place, string type, IBattlegroupEditing editing)
    {
        Point? pressedAt = null;
        // not on the ×, the veterancy strip or the transport button, whose presses are clicks of their own
        tile.PreviewMouseLeftButtonDown += (_, e) => pressedAt = tile.IsOnButton(e.OriginalSource) ? null : e.GetPosition(outline);
        tile.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;
        tile.PreviewMouseMove += (_, e) =>
        {
            if (pressedAt is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            Vector moved = e.GetPosition(outline) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressedAt = null;
            DragDrop.DoDragDrop(outline, new DataObject(typeof(CardPlace), new CardPlace(place, type)), DragDropEffects.Move);
        };
        AcceptDrops(outline, place, type, editing);
    }

    /// <summary>
    /// Lets cards of a row be dropped on <paramref name="target"/>, which stands for a place in the battlegroup: a card
    /// dropped goes there, before the card that was there (the row's end, for its +).
    /// </summary>
    private static void AcceptDrops(Border target, int place, string type, IBattlegroupEditing editing)
    {
        target.AllowDrop = true;
        Rectangle? mark = (target.Child as Grid)?.Children.OfType<Rectangle>().FirstOrDefault();
        CardPlace? Dragged(DragEventArgs e) => e.Data.GetData(typeof(CardPlace)) is CardPlace dragged && dragged.Type == type ? dragged : null;
        target.DragOver += (_, e) =>
        {
            e.Effects = Dragged(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
            if (mark is not null) mark.Visibility = e.Effects == DragDropEffects.Move ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        };
        target.DragLeave += (_, _) => { if (mark is not null) mark.Visibility = Visibility.Collapsed; };
        target.Drop += (_, e) =>
        {
            if (mark is not null) mark.Visibility = Visibility.Collapsed;
            if (Dragged(e) is not CardPlace dragged) return;
            e.Handled = true;
            // the cards after the one taken out move up one, so a place after it is one less
            int to = dragged.Place < place ? place - 1 : place;
            if (to != dragged.Place) editing.MoveCard(dragged.Place, to);
        };
    }

    /// <summary>
    /// The + at a row's end, dashed as the division's cost buttons' +: a click opens Add a Card for the row's type, and
    /// a card dropped on it goes to the row's end.
    /// </summary>
    private static Border AddTile(string type, int end, Action<FrameworkElement, string> addClicked, IBattlegroupEditing editing)
    {
        var plus = new TextBlock { Text = "+", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var dashes = new Rectangle { StrokeThickness = 1, StrokeDashArray = [4, 3] };
        dashes.SetResourceReference(Shape.StrokeProperty, "hi");
        var holder = new Grid { Children = { dashes, plus, DropMark() }, MinHeight = 70, Background = System.Windows.Media.Brushes.Transparent };
        var tile = new Border { Child = holder, Margin = new Thickness(CardRowView.Gap / 2), Cursor = Cursors.Hand, ToolTip = Texts.Get("Divisions.Battlegroup.AddCard.Tip") };
        tile.MouseEnter += (_, _) =>
        {
            plus.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            dashes.SetResourceReference(Shape.StrokeProperty, "accent");
        };
        tile.MouseLeave += (_, _) =>
        {
            plus.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            dashes.SetResourceReference(Shape.StrokeProperty, "hi");
        };
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            addClicked(tile, type);
        };
        AcceptDrops(tile, end, type, editing);
        return tile;
    }

    /// <summary>The units in a card, as its division gives them at its veterancy; "?" when the division does not have the unit.</summary>
    private static string UnitsInCard(BattlegroupCard card, Division division)
    {
        DivisionUnit? rule = division.Units.FirstOrDefault(each => each.Unit == card.Unit);
        if (rule?.UnitsPerCard is not int perCard || card.Veterancy < 0 || card.Veterancy >= rule.VeterancyMultipliers.Count) return "?";
        return Texts.Number(DivisionNames.UnitsAt(perCard, rule.VeterancyMultipliers[card.Veterancy]));
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }
}
