using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// A unit as a raised tile, for the Cards view: its picture, its cost on the picture's top right corner as the
/// game draws it, its name in bold and its nation under it. Under the mouse the name turns the accent colour; selected, the
/// tile is sunk, on the selection colour. In a division, the picture's bottom-left corner has the units in each card,
/// as the game shows them, and the name has the number of cards before it.
/// </summary>
public sealed class UnitTile : Grid
{
    private readonly Bevel tile = new() { Padding = new Thickness(4) };
    private readonly UnitPicture picture = new();
    private readonly TextBlock name = new() { FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };

    /// <summary>A division's number of cards of the unit, "[2] ", dim before the name; empty elsewhere.</summary>
    private readonly Run cards = new() { FontWeight = FontWeights.Normal };

    private readonly Run nameText = new();

    /// <summary>A division's units in each card, "×4", on the picture's bottom-left corner; hidden elsewhere.</summary>
    private readonly UnitsPerCardMark unitsPerCard = new()
    {
        // 3 pixels in from the picture's corner, as the game has it
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(3, 0, 0, 3),
        Visibility = Visibility.Collapsed,
    };
    private readonly NationMark nationMark;
    private readonly TextBlock nation = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
    /// <summary>The cost, on the picture's top right corner, as the game draws it.</summary>
    private readonly UnitCostMark cost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, CostInset, CostInset, 0),
    };

    /// <summary>How far the cost is from the picture's top right corner (the game's is 3, on a smaller card).</summary>
    private const double CostInset = 4;

    /// <summary>The picture, with a division's veterancy strip at its right when it has one.</summary>
    private readonly DockPanel pictureRow = new();

    /// <summary>The picture and what is drawn over it.</summary>
    private readonly Grid pictureArea;

    /// <summary>The Add Units window's tick box on the picture's top-left corner; null elsewhere.</summary>
    private TextBlock? tickMark;
    private Bevel? tickBox;

    /// <summary>"Already added", across the picture's foot, for a unit the Add Units window cannot pick again; null elsewhere.</summary>
    private Border? alreadyBand;

    /// <summary>The gap between the picture and the veterancy strip.</summary>
    private const double StripGap = 3;
    private readonly UnitSelection selection;

    /// <summary>The unit shown.</summary>
    public Unit Unit { get; private set; }

    /// <summary>Whether the open mod changed the unit's name: then the name is in the changed values' colour always.</summary>
    /// <summary>The "*" after the name when the open mod changed it; null when it did not.</summary>
    private System.Windows.Documents.Run? nameStar;

    /// <summary>Builds the tile of a unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="firstSide">Whether the unit is on the game's first side (NATO, the Allies), for its nation's mark.</param>
    /// <param name="selection">Which units are selected.</param>
    public UnitTile(Unit unit, bool firstSide, UnitSelection selection)
    {
        Unit = unit;
        this.selection = selection;
        Cursor = Cursors.Hand;
        Background = System.Windows.Media.Brushes.Transparent;

        nation.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        nationMark = new NationMark(unit.Nation, firstSide);
        var nationLine = new DockPanel { Margin = new Thickness(0, 2, 0, 0), Children = { nationMark, nation } };
        cards.SetResourceReference(TextElement.ForegroundProperty, "muted");
        name.Inlines.Add(cards);
        name.Inlines.Add(nameText);
        pictureArea = new Grid { Children = { picture, unitsPerCard, cost } };
        pictureRow.Children.Add(pictureArea);
        tile.Child = new StackPanel { Children = { pictureRow, name, nationLine } };
        Children.Add(tile);
        Show(unit, firstSide);

        MouseEnter += (_, _) => ShowState();
        MouseLeave += (_, _) => ShowState();
        // the tile follows the selection while it is on screen
        Loaded += (_, _) => { selection.Changed += ShowState; ShowState(); };
        Unloaded += (_, _) => selection.Changed -= ShowState;
    }

    /// <summary>
    /// Shows a unit in the tile. The list reuses its rows while it scrolls, so a tile shows one unit after another
    /// instead of a new tile being built for each.
    /// </summary>
    public void Show(Unit unit, bool firstSide)
    {
        Unit = unit;
        picture.Source = unit.PictureSource;
        picture.Factory = unit.Factory;
        nameText.Text = unit.Name;
        // the name's "*" after it, in the changed values' colour, the name in its own
        if (nameStar is not null) name.Inlines.Remove(nameStar);
        nameStar = ModMarks.IsMarked(unit, UnitField.Name) ? ModMarks.Star() : null;
        if (nameStar is not null) name.Inlines.Add(nameStar);
        name.ToolTip = ModMarks.IsChanged(unit, UnitField.Name) && ModMarks.Changes.Of(unit) is UnitChange change ? ModMarks.TipFor(change, UnitField.Name, game => game.Name) : unit.Name;
        nationMark.Show(unit.Nation, firstSide);
        nation.Text = UnitNames.Nation(unit.Nation);
        ShowCost(unit);
        ShowState();
    }

    /// <summary>
    /// A division's tile: the units in each card on the picture's corner, in the game's font, and the number of cards
    /// before the name, dim, each with its tooltip.
    /// </summary>
    /// <param name="cardCount">The number of cards, such as "2".</param>
    /// <param name="unitsInEach">The units in each card, such as "4".</param>
    /// <param name="cardsTip">What the number of cards is, in full.</param>
    /// <param name="unitsTip">What the units in each card are, in full.</param>
    public void ShowCards(string cardCount, string unitsInEach, string cardsTip, string unitsTip)
    {
        cards.Text = "[" + cardCount + "] ";
        cards.ToolTip = cardsTip;
        unitsPerCard.Number = unitsInEach;
        unitsPerCard.ToolTip = unitsTip;
        unitsPerCard.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// A battlegroup's card, laid out as a division's tile: the units in the card on the picture's bottom-left corner,
    /// and its transport ("in M113A2") in place of the nation. Its veterancy and transport are chosen on the strip and
    /// the button at the picture's right (<see cref="ShowVeterancyChoice"/>, <see cref="ShowTransports"/>).
    /// </summary>
    /// <param name="unitsInCard">The units in the card, such as "4".</param>
    /// <param name="transport">The transport's name; null for none, when the nation shows as on other tiles.</param>
    /// <param name="tip">What the card is, in full: the tile's tooltip.</param>
    public void ShowBattlegroupCard(string unitsInCard, string? transport, string tip)
    {
        unitsPerCard.Number = unitsInCard;
        unitsPerCard.Visibility = Visibility.Visible;
        if (transport is not null) nation.Text = Texts.Get("Divisions.Battlegroup.InTransport", ("transport", transport));
        ToolTip = tip;
    }

    /// <summary>
    /// A battlegroup card's veterancy strip at the picture's right, as a division's tile has (see
    /// <see cref="Divisions.VeterancyMarks.ChoiceStrip"/>): a click on a level gives the card that level.
    /// </summary>
    public void ShowVeterancyChoice(IReadOnlyList<int> counts, int chosen, Action<int> choose) =>
        Side().Children.Add(Divisions.VeterancyMarks.ChoiceStrip(counts, chosen, choose));

    /// <summary>The × of <see cref="ShowTakeOut"/>; null for a tile without one.</summary>
    private Bevel? takeOut;

    /// <summary>
    /// Whether an element is one of the tile's buttons, or in one: the ×, the veterancy strip or the transport button.
    /// A press there is the button's, not the start of a drag.
    /// </summary>
    public bool IsOnButton(object? element)
    {
        // Walk up from the pressed element. It can be a Run inside the name's text, which is not a Visual, so the
        // walk takes its logical parent until it reaches the visual tree.
        for (DependencyObject? at = element as DependencyObject; at is not null && at != this; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at == takeOut || at == side) return true;
        }
        return false;
    }

    /// <summary>
    /// A small raised square with × on the tile's bottom-right corner, under the picture (clear of the veterancy strip),
    /// shown while the mouse is over the tile: a click takes the card out of its battlegroup, or the unit out of its
    /// division. Under the mouse it takes the hover colour,
    /// its × the warning colour; pressed, it sinks. Its click is its own: it does not select the tile as well.
    /// </summary>
    /// <param name="tip">What a click does.</param>
    /// <param name="clicked">What a click does.</param>
    public void ShowTakeOut(string tip, Action clicked)
    {
        var cross = new TextBlock
        {
            Text = "×", FontSize = 13, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0),
        };
        cross.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var button = new Bevel
        {
            Child = cross, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, ToolTip = tip,
        };
        void Raised(string background)
        {
            button.SetResourceReference(Bevel.TopLeftProperty, "hi");
            button.SetResourceReference(Bevel.BottomRightProperty, "lo");
            button.SetResourceReference(Bevel.BackgroundProperty, background);
        }
        Raised("panel");
        button.MouseEnter += (_, _) =>
        {
            Raised("hover");
            cross.SetResourceReference(TextBlock.ForegroundProperty, "warn");
        };
        button.MouseLeave += (_, _) =>
        {
            Raised("panel");
            cross.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        };
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            button.SetResourceReference(Bevel.TopLeftProperty, "lo");
            button.SetResourceReference(Bevel.BottomRightProperty, "hi");
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Raised("hover");
            clicked();
        };
        Children.Add(button);
        takeOut = button;
        MouseEnter += (_, _) => button.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => button.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The cards and units in each card as values the open mod changed: in the headings' colour, their tooltips saying
    /// what changed them. Unlike the other changed values, without "*" (Paul's choice: it cluttered the tile); the unit's
    /// pop-up, where they are typed, has it.
    /// </summary>
    public void MarkCards(string tip)
    {
        cards.ToolTip = tip;
        unitsPerCard.ToolTip = tip;
        if (!ModMarks.AreShown) return;
        cards.SetResourceReference(TextElement.ForegroundProperty, ModMarks.ChangedColour);
        cards.ToolTip = tip;
        unitsPerCard.SetResourceReference(UnitsPerCardMark.ForegroundProperty, ModMarks.ChangedColour);
        unitsPerCard.ToolTip = tip;
    }

    /// <summary>
    /// A division's tile: the veterancy strip at the picture's right (<see cref="Divisions.VeterancyMarks.Strip"/>),
    /// the picture made smaller to fit beside it rather than covered. The cost stays on the picture's corner.
    /// </summary>
    /// <param name="counts">The units per card at each level, lowest first.</param>
    /// <param name="clicked">What a click on the strip does, given the strip: the pop-up for the unit in the division.</param>
    public void ShowVeterancy(IReadOnlyList<int> counts, Action<FrameworkElement> clicked)
    {
        Bevel strip = Divisions.VeterancyMarks.Strip(counts);
        strip.Cursor = Cursors.Hand;
        strip.ToolTip = Texts.Get("Divisions.Veterancy.Edit.Tip");
        // the click is the strip's own: it does not select the tile as well
        strip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            clicked(strip);
        };
        // the strip fills the column, above the transport button when there is one
        Side().Children.Add(strip);
    }

    /// <summary>
    /// The Add Units window's tile: a tick box on the picture's top-left corner, ticked while the unit is (it is the
    /// window's selection), as its list's rows have; or, for a unit chosen before, the tile dim with "Already added"
    /// across the picture and no box, as it cannot be picked again.
    /// </summary>
    /// <param name="alreadyChosen">Whether the unit was chosen before.</param>
    /// <param name="alreadyText">The words for that: "Already added".</param>
    public void ShowTick(bool alreadyChosen, string alreadyText)
    {
        if (tickBox is null)
        {
            tickMark = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tickMark.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            tickBox = new Bevel { Child = tickMark, Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
            tickBox.SetResourceReference(Bevel.TopLeftProperty, "lo");
            tickBox.SetResourceReference(Bevel.BottomRightProperty, "hi");
            tickBox.SetResourceReference(Bevel.BackgroundProperty, "well");
            var words = new TextBlock { FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) };
            words.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            alreadyBand = new Border
            {
                Child = words, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0, 0, 0)),
            };
            pictureArea.Children.Add(tickBox);
            pictureArea.Children.Add(alreadyBand);
        }
        ((TextBlock)alreadyBand!.Child).Text = alreadyText;
        tickBox.Visibility = alreadyChosen ? Visibility.Collapsed : Visibility.Visible;
        alreadyBand.Visibility = alreadyChosen ? Visibility.Visible : Visibility.Collapsed;
        Opacity = alreadyChosen ? 0.45 : 1;
        Cursor = alreadyChosen ? Cursors.Arrow : Cursors.Hand;
        ShowState();
    }

    /// <summary>The truck drawn on the transport button: a box body, a cab, and two wheels.</summary>
    private static readonly System.Windows.Media.Geometry TruckIcon = System.Windows.Media.Geometry.Parse(
        "M 1.5,9.5 V 4.5 H 8.5 V 9.5 M 8.5,6.5 H 11 L 12.5,8.5 V 9.5 H 1.5 "
        + "M 2.7,10.5 A 1.3,1.3 0 1 1 5.3,10.5 A 1.3,1.3 0 1 1 2.7,10.5 Z M 9.2,10.5 A 1.3,1.3 0 1 1 11.8,10.5 A 1.3,1.3 0 1 1 9.2,10.5 Z");

    /// <summary>
    /// A division's tile, for a unit that can ride in a transport: a small truck button under the veterancy strip, lit
    /// when the unit has transports and dim when it has none, its tooltip saying how many of each kind. A click opens
    /// the unit's transports pop-up beside it.
    /// </summary>
    /// <param name="hasTransports">Whether the unit has any transport in the division.</param>
    /// <param name="tip">The button's tooltip.</param>
    /// <param name="clicked">What a click does, given the button: the transports pop-up.</param>
    public void ShowTransports(bool hasTransports, string tip, Action<FrameworkElement> clicked)
    {
        var icon = new System.Windows.Shapes.Path { Data = TruckIcon, Width = 14, Height = 14, StrokeThickness = 1.3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, hasTransports ? "ink" : "muted");
        if (!hasTransports) icon.Opacity = 0.6;
        var button = new Bevel { Child = icon, Height = 18, Margin = new Thickness(0, 3, 0, 0), Cursor = Cursors.Hand, ToolTip = tip };
        button.SetResourceReference(Bevel.TopLeftProperty, "lo");
        button.SetResourceReference(Bevel.BottomRightProperty, "hi");
        button.SetResourceReference(Bevel.BackgroundProperty, "well");
        button.MouseEnter += (_, _) => button.SetResourceReference(Bevel.BackgroundProperty, "hover");
        button.MouseLeave += (_, _) => button.SetResourceReference(Bevel.BackgroundProperty, "well");
        // the click is the button's own: it does not select the tile as well
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            clicked(button);
        };
        DockPanel.SetDock(button, Dock.Bottom);
        // before the strip, which fills what is left of the column
        Side().Children.Insert(0, button);
    }

    /// <summary>The column at the picture's right that holds the veterancy strip and the transport button.</summary>
    private DockPanel? side;

    /// <summary>The column at the picture's right, made the first time; the picture is made smaller to fit beside it.</summary>
    private DockPanel Side()
    {
        if (side is not null) return side;
        side = new DockPanel { Width = Divisions.VeterancyMarks.StripWidth, Margin = new Thickness(StripGap, 0, 0, 0) };
        DockPanel.SetDock(side, Dock.Right);
        pictureRow.Children.Insert(0, side);
        return side;
    }

    /// <summary>
    /// The cost on the picture's corner; nothing for a unit with none. Its tooltip is the cost's name, or what changed
    /// it when the open mod did.
    /// </summary>
    private void ShowCost(Unit unit)
    {
        ModMarks.MarkedValue shown = ModMarks.Mark(unit, UnitField.Cost, unit.Cost is null ? "" : UnitNumbers.Of(unit.Cost),
            game => game.Cost is null ? "" : UnitNumbers.Of(game.Cost), tip: Texts.Get("Units.Stat.Cost"));
        cost.Number = shown.Text;
        cost.IsChanged = shown.IsChanged;
        cost.ToolTip = shown.Tip;
    }

    /// <summary>Raised, or sunk on the selection colour when selected; the name in the accent colour when selected or under the mouse.</summary>
    private void ShowState()
    {
        bool isSelected = selection.IsSelected(Unit);
        tile.SetResourceReference(Bevel.TopLeftProperty, isSelected ? "lo" : "hi");
        tile.SetResourceReference(Bevel.BottomRightProperty, isSelected ? "hi" : "lo");
        tile.SetResourceReference(Bevel.BackgroundProperty, isSelected ? "sel" : "panel");
        name.SetResourceReference(TextBlock.ForegroundProperty, isSelected || IsMouseOver ? "accent" : "ink");
        if (tickMark is not null) tickMark.Text = isSelected ? "✓" : "";
    }
}
