using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Dialogs;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// Add a Card, opened from the + at the end of a battlegroup's row: the division's units of that type, in the game's
/// order, as the Armory's tiles (Cards) or as rows with their pictures (Table), as the Add Units window shows them; the
/// view chosen is kept for next time. A click picks a unit; under the list, its veterancy levels (as marks with the
/// units in a card at each) and, for a unit that rides in one, its transport. Add (or Enter, or a double-click) adds
/// the card, and the pop-up stays open for the next. A unit the battlegroup already has as many cards of as the
/// division allows is dim and cannot be picked. Close, Esc or a click outside closes it.
/// </summary>
public sealed class BattlegroupCardPopup
{
    /// <summary>The pop-up's width, and the height of its list, which scrolls.</summary>
    private const double Width = 520, ListHeight = 330;

    private readonly FloatingPanel floating;
    private readonly string type;
    private readonly IReadOnlyDictionary<string, Unit> units;
    private readonly IBattlegroupEditing editing;
    private readonly Func<Unit, bool> isFirstSide;

    /// <summary>The division as it is now: read again after each card added, for the counts.</summary>
    private readonly Func<Division?> division;

    private readonly ContentControl list = new() { Focusable = false };
    private readonly StackPanel choices = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button add = new() { Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
    private readonly ToggleButton cardsButton = new();
    private readonly ToggleButton tableButton = new();

    /// <summary>The unit picked, as the tiles show their selection.</summary>
    private readonly UnitSelection pickedTile = new();

    /// <summary>Whether the units show as tiles rather than as rows; kept from one opening to the next.</summary>
    private bool showsCards = (string?)AppData.Current.State["addCardView"] == "cards";

    // the card being picked: its unit (null until one is), level and transport
    private DivisionUnit? picked;
    private int pickedLevel;
    private string? pickedTransport;

    /// <summary>Opens the pop-up at the right of a row's +.</summary>
    /// <param name="button">The + clicked.</param>
    /// <param name="type">The row's type, such as "Tanks".</param>
    /// <param name="division">The division as it is now, read again after each card added.</param>
    /// <param name="units">The source's units by descriptor.</param>
    /// <param name="isFirstSide">Whether a unit is on the game's first side, for its nation's mark.</param>
    /// <param name="editing">What adds the card, in the open mod.</param>
    public static void Open(FrameworkElement button, string type, Func<Division?> division, IReadOnlyDictionary<string, Unit> units, Func<Unit, bool> isFirstSide,
        IBattlegroupEditing editing) =>
        _ = new BattlegroupCardPopup(button, type, division, units, isFirstSide, editing);

    private BattlegroupCardPopup(FrameworkElement button, string type, Func<Division?> division, IReadOnlyDictionary<string, Unit> units, Func<Unit, bool> isFirstSide,
        IBattlegroupEditing editing)
    {
        this.type = type;
        this.division = division;
        this.units = units;
        this.isFirstSide = isFirstSide;
        this.editing = editing;
        floating = MainView.Of(button).Floating;
        // a double-click adds the unit picked by its first click: heard by the list, as picking draws it again
        list.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) AddPicked();
        };

        var parts = new StackPanel();
        parts.Children.Add(Heading(UnitNames.Factory(type), division()?.Name ?? ""));
        var scroller = new ScrollViewer { Content = list, Height = ListHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var well = new Bevel { Child = scroller };
        well.SetResourceReference(Bevel.TopLeftProperty, "lo");
        well.SetResourceReference(Bevel.BottomRightProperty, "hi");
        well.SetResourceReference(Bevel.BackgroundProperty, "well");
        parts.Children.Add(well);
        parts.Children.Add(choices);
        parts.Children.Add(Foot());

        var box = new Bevel { Child = parts, Padding = new Thickness(9, 7, 9, 8), Width = Width, Focusable = true, FocusVisualStyle = null };
        box.SetResourceReference(Bevel.TopLeftProperty, "hi");
        box.SetResourceReference(Bevel.BottomRightProperty, "lo");
        box.SetResourceReference(Bevel.BackgroundProperty, "panel");
        box.PreviewKeyDown += KeyPressed;
        ShowViewButtons();
        ShowList();
        ShowChoices();
        floating.Show(box, button);
        // the keyboard goes to the pop-up, so Enter adds and Esc closes at once
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => box.Focus());
    }

    /// <summary>
    /// "Add a Card" in bold, the type and division dim under it, and at the right Cards and Table, as the Add Units
    /// window's.
    /// </summary>
    private DockPanel Heading(string typeName, string divisionName)
    {
        var title = new TextBlock { Text = Texts.Get("Divisions.Battlegroup.AddCard"), FontWeight = FontWeights.Bold };
        var sub = new TextBlock { Text = typeName + " · " + divisionName, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        ViewButton(cardsButton, ViewShape.Cards, Texts.Get("Units.Cards"), Texts.Get("Units.AddUnits.Cards.Tip"), new Thickness(0));
        ViewButton(tableButton, ViewShape.Table, Texts.Get("Units.Table"), Texts.Get("Units.AddUnits.Table.Tip"), new Thickness(-1, 0, 0, 0));
        var views = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { cardsButton, tableButton } };
        DockPanel.SetDock(views, Dock.Right);
        return new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { views, new StackPanel { Children = { title, sub } } } };
    }

    /// <summary>One of the view buttons: the Armory's icon, its name for screen readers, and its tooltip.</summary>
    private void ViewButton(ToggleButton button, ViewShape shape, string name, string tip, Thickness margin)
    {
        button.Style = (Style)Application.Current.FindResource("PressedToggle");
        button.Padding = new Thickness(5, 4, 5, 4);
        button.Margin = margin;
        button.Content = new ViewIcon { Shape = shape };
        button.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        bool cards = shape == ViewShape.Cards;
        button.Click += (_, _) =>
        {
            showsCards = cards;
            AppData.Current.State["addCardView"] = cards ? "cards" : "table";
            AppData.Current.SaveState();
            ShowViewButtons();
            ShowList();
        };
    }

    /// <summary>The view shown stays pressed.</summary>
    private void ShowViewButtons()
    {
        cardsButton.IsChecked = showsCards;
        tableButton.IsChecked = !showsCards;
    }

    /// <summary>Add and Close, at the right.</summary>
    private StackPanel Foot()
    {
        add.Content = Texts.Get("Divisions.Battlegroup.AddCard.Add");
        add.Click += (_, _) => AddPicked();
        var close = new Button { Content = Texts.Get("Divisions.Battlegroup.AddCard.Close"), Margin = new Thickness(6, 0, 0, 0) };
        close.Click += (_, _) => floating.Close();
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { add, close } };
    }

    // ---- the list ----

    /// <summary>
    /// The division's units of the type, in the game's order, as tiles or rows, each with the cards of it the
    /// battlegroup has now.
    /// </summary>
    private void ShowList()
    {
        Division? now = division();
        if (now is null) return;
        IReadOnlyList<BattlegroupCard> cards = now.Battlegroup?.Cards ?? [];
        List<DivisionUnit> ofType = UnitGameOrder.Sort(
            now.Units.Where(rule => units.TryGetValue(rule.Unit, out Unit? unit) && unit.Factory == type),
            rule => units[rule.Unit], units);
        add.IsEnabled = picked is not null;
        if (ofType.Count == 0)
        {
            var none = new TextBlock { Text = Texts.Get("Divisions.Battlegroup.AddCard.None", ("division", now.Name)), Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
            none.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            list.Content = none;
            return;
        }
        if (showsCards)
        {
            // as many tiles across as fit inside the pop-up's padding, beside the scrollbar
            var tiles = new UniformGrid { Columns = CardRowView.ColumnsFor(Width - 18 - 14), Margin = new Thickness(CardRowView.Gap / 2) };
            foreach (DivisionUnit rule in ofType) tiles.Children.Add(Tile(rule, CardsOf(cards, rule)));
            list.Content = tiles;
            return;
        }
        var rows = new StackPanel();
        foreach (DivisionUnit rule in ofType) rows.Children.Add(Row(rule, CardsOf(cards, rule)));
        list.Content = rows;
    }

    /// <summary>How many cards of a unit the battlegroup has.</summary>
    private static int CardsOf(IReadOnlyList<BattlegroupCard> cards, DivisionUnit rule) => cards.Count(card => card.Unit == rule.Unit);

    /// <summary>
    /// A unit as the Armory's tile, sunk while picked, with its cards in the battlegroup of how many the division allows
    /// before its name; dim when it has as many as allowed.
    /// </summary>
    private FrameworkElement Tile(DivisionUnit rule, int cardsNow)
    {
        Unit unit = units[rule.Unit];
        var tile = new UnitTile(unit, isFirstSide(unit), pickedTile) { Margin = new Thickness(CardRowView.Gap / 2) };
        tile.ShowCards(Texts.Number(cardsNow) + "/" + Texts.Number(rule.Cards), DivisionNames.UnitsPerCard(rule),
            CardsTip(unit, cardsNow, rule), Texts.Get("Divisions.Card.UnitsPerCard.Tip", ("units", DivisionNames.UnitsPerCard(rule))));
        MakePickable(tile, rule, cardsNow);
        return tile;
    }

    /// <summary>
    /// A unit's row, as in the Add Units window: its picture and nation's mark, then its name and, dim under it, its
    /// cost and how many cards the battlegroup has of how many the division allows. Picked, the row is on the hover colour.
    /// </summary>
    private FrameworkElement Row(DivisionUnit rule, int cardsNow)
    {
        Unit unit = units[rule.Unit];
        var name = new TextBlock { Text = unit.Name, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock { Text = CardsTip(unit, cardsNow, rule), FontSize = 11 };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Children = { name, detail } };
        var picture = new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory, Width = 54 };
        var mark = new NationMark(unit.Nation, isFirstSide(unit)) { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(picture, Dock.Left);
        DockPanel.SetDock(mark, Dock.Left);
        var row = new Bevel { Child = new DockPanel { Children = { picture, mark, words } }, Padding = new Thickness(6, 2, 6, 2), Edges = new Thickness(0, 0, 0, 1) };
        row.SetResourceReference(Bevel.BottomRightProperty, "line");
        bool isPicked = picked?.Unit == rule.Unit;
        if (isPicked) row.SetResourceReference(Bevel.BackgroundProperty, "hover");
        else row.Background = System.Windows.Media.Brushes.Transparent;
        bool isFull = cardsNow >= rule.Cards;
        row.MouseEnter += (_, _) => { if (!isPicked && !isFull) row.SetResourceReference(Bevel.BackgroundProperty, "sel"); };
        row.MouseLeave += (_, _) => { if (!isPicked) row.Background = System.Windows.Media.Brushes.Transparent; };
        MakePickable(row, rule, cardsNow);
        return row;
    }

    /// <summary>"275 pts · 3 of 6 cards": the unit's cost, and its cards in the battlegroup of how many the division allows.</summary>
    private static string CardsTip(Unit unit, int cardsNow, DivisionUnit rule) =>
        Texts.Get("Divisions.Battlegroup.AddCard.Units", ("cost", UnitNumbers.Of(unit.Cost)), ("cards", Texts.Number(cardsNow)), ("allowed", Texts.Number(rule.Cards)));

    /// <summary>
    /// A click on a tile or row picks its unit; a unit with as many cards as the division allows is dim, and says so
    /// rather than being picked.
    /// </summary>
    private void MakePickable(FrameworkElement element, DivisionUnit rule, int cardsNow)
    {
        if (cardsNow >= rule.Cards)
        {
            element.Opacity = 0.45;
            element.ToolTip = Texts.Get("Divisions.Battlegroup.AddCard.Full");
            element.Cursor = Cursors.Arrow;
            return;
        }
        element.Cursor = Cursors.Hand;
        element.MouseLeftButtonUp += (_, _) => Pick(rule);
    }

    // ---- the unit picked: its veterancy and transport ----

    /// <summary>
    /// Under the list, the unit picked: its name, its veterancy levels (each its mark and the units in a card at it, the
    /// one chosen pressed, a level the division does not offer dim), then its transport, for a unit that rides in one.
    /// </summary>
    private void ShowChoices()
    {
        choices.Children.Clear();
        if (picked is not DivisionUnit rule)
        {
            choices.Children.Add(Label(Texts.Get("Divisions.Battlegroup.AddCard.Pick"), new Thickness(0)));
            return;
        }
        var line = new WrapPanel();
        line.Children.Add(Label(Texts.Get("Divisions.Battlegroup.AddCard.Veterancy"), new Thickness(0, 0, 8, 0)));
        line.Children.Add(Levels(rule));
        if (TransportChoice(rule) is ComboBox transports)
        {
            line.Children.Add(Label(Texts.Get("Divisions.Battlegroup.AddCard.Transport"), new Thickness(16, 0, 8, 0)));
            line.Children.Add(transports);
        }
        choices.Children.Add(new TextBlock { Text = units[rule.Unit].Name, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        choices.Children.Add(line);
    }

    /// <summary>Dim words: a label in the choices, or the hint before a unit is picked.</summary>
    private static TextBlock Label(string text, Thickness margin)
    {
        var label = new TextBlock { Text = text, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return label;
    }

    /// <summary>The unit's veterancy levels side by side, each its mark and the units in a card at it.</summary>
    private StackPanel Levels(DivisionUnit rule)
    {
        var levels = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int level = 0; level < rule.VeterancyMultipliers.Count; level++)
        {
            double share = rule.VeterancyMultipliers[level];
            int inCard = DivisionNames.UnitsAt(rule.UnitsPerCard ?? 1, share);
            var count = new TextBlock { Text = "×" + Texts.Number(inCard), FontSize = 11, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var button = new ToggleButton
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { VeterancyMarks.Mark(level, 11), count } },
                Style = (Style)Application.Current.FindResource("PressedToggle"), Padding = new Thickness(4, 1, 5, 1),
                Margin = new Thickness(levels.Children.Count == 0 ? 0 : -1, 0, 0, 0),
                IsChecked = pickedLevel == level, IsEnabled = share > 0,
                ToolTip = share > 0
                    ? Texts.Get("Divisions.Battlegroup.AddCard.Veterancy.Tip", ("level", level), ("units", inCard))
                    : Texts.Get("Divisions.Battlegroup.AddCard.NotOffered.Tip", ("level", level)),
            };
            ToolTipService.SetShowOnDisabled(button, true);
            int chosen = level;
            button.Click += (_, _) =>
            {
                pickedLevel = chosen;
                ShowChoices();
            };
            levels.Children.Add(button);
        }
        return levels;
    }

    /// <summary>
    /// The unit's transport: "No transport" when the division lets it go without one, then each transport the division
    /// gives it, by name; null for a unit with none to choose from.
    /// </summary>
    private ComboBox? TransportChoice(DivisionUnit rule)
    {
        if (rule.Transports.Count == 0) return null;
        var choice = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center, ToolTip = Texts.Get("Divisions.Battlegroup.AddCard.Transport.Tip") };
        if (rule.AvailableWithoutTransport) choice.Items.Add(new ComboBoxItem { Content = Texts.Get("Divisions.Battlegroup.AddCard.NoTransport"), Tag = null });
        foreach (string transport in rule.Transports)
            choice.Items.Add(new ComboBoxItem { Content = units.TryGetValue(transport, out Unit? ride) ? ride.Name : transport, Tag = transport });
        choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag == pickedTransport) ?? choice.Items[0];
        pickedTransport = (string?)((ComboBoxItem)choice.SelectedItem).Tag;
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedItem is ComboBoxItem item) pickedTransport = (string?)item.Tag;
        };
        return choice;
    }

    /// <summary>
    /// Picks a unit: a unit picked anew starts at its lowest level offered, with no transport when it can go without
    /// one, else its first.
    /// </summary>
    private void Pick(DivisionUnit rule)
    {
        if (picked?.Unit != rule.Unit)
        {
            pickedLevel = Math.Max(0, rule.VeterancyMultipliers.ToList().FindIndex(share => share > 0));
            pickedTransport = rule.AvailableWithoutTransport || rule.Transports.Count == 0 ? null : rule.Transports[0];
        }
        picked = rule;
        pickedTile.Selected = units[rule.Unit];
        ShowList();
        ShowChoices();
    }

    /// <summary>Adds the card picked; the pop-up stays open for the next, its counts as they are now.</summary>
    private void AddPicked()
    {
        if (picked is not DivisionUnit rule) return;
        if (!editing.AddCard(new BattlegroupCard(rule.Unit, pickedLevel, pickedTransport))) return;
        // the unit may now have as many cards as allowed: it is let go of, so it cannot be added once more
        Division? now = division();
        if (now is not null && CardsOf(now.Battlegroup?.Cards ?? [], rule) >= rule.Cards)
        {
            picked = null;
            pickedTile.Selected = null;
        }
        ShowList();
        ShowChoices();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            floating.Close();
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not ComboBox)
        {
            e.Handled = true;
            AddPicked();
        }
    }
}
