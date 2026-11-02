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
        name.ToolTip = ModMarks.IsChanged(unit, UnitField.Name) && ModMarks.Changes.Of(unit) is UnitChange change ? unit.Name + Environment.NewLine + ModMarks.TipFor(change, UnitField.Name, game => game.Name) : unit.Name;
        nationMark.Show(unit.Nation, firstSide);
        nation.Text = UnitNames.Nation(unit.Nation);
        ShowCost(unit);
        ShowMen(unit);
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
    /// <param name="tips">Each level's tooltip, lowest first, where its count alone does not say it (Steel Division 2's, by phase).</param>
    public void ShowVeterancy(IReadOnlyList<int> counts, Action<FrameworkElement> clicked, IReadOnlyList<string>? tips = null)
    {
        Bevel strip = Divisions.VeterancyMarks.Strip(counts, tips);
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
    /// The Division Generator's sample card: a strip of the veterancy marks with the units at each level written beside
    /// them (<see cref="Divisions.VeterancyMarks.CountStrip"/>), in place of the one shown before. The column is as wide
    /// as the strip needs.
    /// </summary>
    /// <param name="counts">What to write beside each level's mark, lowest first: a number, "?" or nothing.</param>
    public void ShowVeterancyCounts(IReadOnlyList<string> counts)
    {
        DockPanel column = Side();
        column.Width = double.NaN;
        column.Children.Clear();
        column.Children.Add(Divisions.VeterancyMarks.CountStrip(counts));
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
            words.SetResourceReference(TextBlock.ForegroundProperty, "ink");
            // the theme's panel colour under the theme's ink, so the words read in light and dark themes alike
            alreadyBand = new Border { Child = words, VerticalAlignment = VerticalAlignment.Bottom };
            alreadyBand.SetResourceReference(Border.BackgroundProperty, "panel");
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

    /// <summary>
    /// The column at the picture's right, made the first time; the picture is made smaller to fit beside it. The column
    /// is as tall as the picture and no taller, so every tile is the same size: a strip with the transport button under
    /// it fits in the picture's height, its marks a little closer, rather than stretching the picture.
    /// </summary>
    private DockPanel Side()
    {
        if (side is not null) return side;
        side = new DockPanel { Width = Divisions.VeterancyMarks.StripWidth, Margin = new Thickness(StripGap, 0, 0, 0) };
        var asTallAsThePicture = new HeightTaken { Child = side };
        DockPanel.SetDock(asTallAsThePicture, Dock.Right);
        pictureRow.Children.Insert(0, asTallAsThePicture);
        return side;
    }

    /// <summary>
    /// Holds the column at the picture's right without asking for any height of its own, so the row is as tall as the
    /// picture; the column then fills that height.
    /// </summary>
    private sealed class HeightTaken : Decorator
    {
        protected override Size MeasureOverride(Size constraint)
        {
            if (Child is null) return default;
            // measured as tall as it likes, so the transport button keeps its height; the strip takes what is left
            Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
            return new Size(Child.DesiredSize.Width, 0);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            Child?.Arrange(new Rect(arrangeSize));
            return arrangeSize;
        }
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
        // Steel Division 2's cards have it in a box at the top left instead
        bool inBoxes = CardMarksInBoxes;
        cost.Visibility = inBoxes ? Visibility.Collapsed : Visibility.Visible;
        if (!inBoxes)
        {
            if (boxes is not null) boxes.Visibility = Visibility.Collapsed;
            return;
        }
        Boxes();
        boxes!.Visibility = Visibility.Visible;
        costText!.Text = shown.Text == "" ? "" : shown.Text + (shown.IsChanged ? "*" : "");
        costBox!.Visibility = shown.Text == "" ? Visibility.Collapsed : Visibility.Visible;
        costBox.ToolTip = shown.Tip;
    }

    // ---- Steel Division 2's marks: numbers in boxes at the top left, the phase letter at the top right ----

    /// <summary>Whether the source's game shows its cards' numbers in boxes (Steel Division 2).</summary>
    private static bool CardMarksInBoxes => UnitPictureLibrary.Current?.CardMarksInBoxes == true;

    /// <summary>
    /// The game's colours for its card marks (Colors.ndf): the cost in JauneOrange on black, the units in a card in Cyan
    /// on Gris50, a grey the picture shows through.
    /// </summary>
    private static readonly Brush CostOrange = Frozen(new SolidColorBrush(Color.FromRgb(255, 168, 69))),
        PackCyan = Frozen(new SolidColorBrush(Color.FromRgb(112, 250, 255))), PackGrey = Frozen(new SolidColorBrush(Color.FromArgb(60, 70, 70, 70)));

    /// <summary>The phase letter's silver: white at the top to GrisClair6 (125, 125, 125) at the foot (TextStyleUnlockingPhase).</summary>
    private static readonly Brush PhaseSilver = Frozen(new LinearGradientBrush(Colors.White, Color.FromRgb(125, 125, 125), 90));

    /// <summary>A man's shape, white with a black outline, after the men in each unit, as the game's cards have it.</summary>
    private static readonly Geometry ManShape = Geometry.Parse("M4.5,0 A2,2 0 1 1 4.49,0 Z M1,12 L2,4.5 Q4.5,3.5 7,4.5 L8,12 L6,12 L5.5,8 L3.5,8 L3,12 Z");

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    // the marks, made the first time a tile of such a game needs them
    private StackPanel? boxes;
    private Border? costBox, packBox;
    private OutlinedText? costText, phaseLetter, menText;
    private StackPanel? packNumbers, men;

    /// <summary>
    /// The boxes at the picture's top left, the phase letter at its top right and the men at its bottom right, sized as
    /// the game's are on its cards (UISpecificUnitButtonView.ndf: 30 by 18 boxes, size 14 numbers, a size 42 letter, on
    /// a picture 166 wide), scaled to the tile's picture.
    /// </summary>
    private void Boxes()
    {
        if (boxes is not null) return;
        costText = new OutlinedText { Fill = CostOrange, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        costBox = Box(costText, Brushes.Black);
        packNumbers = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        packBox = Box(packNumbers, PackGrey);
        packBox.Visibility = Visibility.Collapsed;
        packBox.Margin = new Thickness(0);
        boxes = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { costBox } };
        phaseLetter = new OutlinedText
        {
            Fill = PhaseSilver, Size = 32, OutlineWidth = 1.4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0), Visibility = Visibility.Collapsed,
        };
        menText = new OutlinedText { VerticalAlignment = VerticalAlignment.Bottom };
        var man = new System.Windows.Shapes.Path
        {
            Data = ManShape, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Width = 9, Height = 12, Stretch = Stretch.Uniform,
            Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom,
        };
        men = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 3), Visibility = Visibility.Collapsed, Children = { menText, man },
        };
        // the units in a card in their box in the bottom-left corner (Paul's choice), the men at the bottom right
        packBox.HorizontalAlignment = HorizontalAlignment.Left;
        packBox.VerticalAlignment = VerticalAlignment.Bottom;
        pictureArea.Children.Add(boxes);
        pictureArea.Children.Add(phaseLetter);
        pictureArea.Children.Add(packBox);
        pictureArea.Children.Add(men);
    }

    /// <summary>A box of the game's: at least 24 wide and 15 tall, the number centred in it.</summary>
    private static Border Box(UIElement content, Brush background) =>
        new() { Background = background, MinWidth = 24, Height = 15, Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 0, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = content };

    /// <summary>The men in each unit at the picture's bottom right, for infantry, as the game's cards show them.</summary>
    private void ShowMen(Unit unit)
    {
        bool shown = CardMarksInBoxes && unit.ArmorFamilies.Front == "Infanterie" && unit.Strength is double strength && strength >= 1;
        if (!shown)
        {
            if (men is not null) men.Visibility = Visibility.Collapsed;
            return;
        }
        Boxes();
        menText!.Text = UnitNumbers.Of(unit.Strength);
        men!.Visibility = Visibility.Visible;
        men.ToolTip = Texts.Get("Units.Tile.Men.Tip", ("count", menText.Text));
    }

    /// <summary>
    /// A Steel Division 2 division's tile: the units in a card in each phase in a box at the bottom left, "6/12/0", a
    /// phase the card can't be taken in dimmed, and the letter of the first phase it can be taken in at the top right.
    /// Nothing for a game whose cards don't show their numbers in boxes.
    /// </summary>
    /// <param name="unitsByPhase">The units in a card in phases A, B and C.</param>
    /// <param name="tip">What they are, in full.</param>
    /// <param name="chosen">The phase chosen above the cards, 0 to 2, whose number alone is shown, the tile dimmed when it has none; -1 for every phase.</param>
    public void ShowPhases(IReadOnlyList<int> unitsByPhase, string tip, int chosen = -1)
    {
        if (!CardMarksInBoxes || unitsByPhase.Count == 0) return;
        Boxes();
        unitsPerCard.Visibility = Visibility.Collapsed;
        packNumbers!.Children.Clear();
        if (chosen >= 0 && chosen < unitsByPhase.Count)
        {
            packNumbers.Children.Add(new OutlinedText { Text = Texts.Number(unitsByPhase[chosen]), Fill = PackCyan });
            tile.Opacity = unitsByPhase[chosen] == 0 ? 0.38 : 1;
        }
        else
        {
            tile.Opacity = 1;
            for (int phase = 0; phase < unitsByPhase.Count; phase++)
            {
                if (phase > 0) packNumbers.Children.Add(new OutlinedText { Text = "/", Fill = PackCyan, Opacity = 0.6, Margin = new Thickness(1, 0, 1, 0) });
                packNumbers.Children.Add(new OutlinedText { Text = Texts.Number(unitsByPhase[phase]), Fill = PackCyan, Opacity = unitsByPhase[phase] == 0 ? 0.4 : 1 });
            }
        }
        packBox!.Visibility = Visibility.Visible;
        packBox.ToolTip = tip;
        int first = unitsByPhase.ToList().FindIndex(count => count > 0);
        phaseLetter!.Text = first < 0 ? "" : ((char)('A' + first)).ToString();
        phaseLetter.Visibility = first < 0 ? Visibility.Collapsed : Visibility.Visible;
        phaseLetter.ToolTip = first < 0 ? null : Texts.Get("Divisions.Tile.Phase.Tip", ("phase", phaseLetter.Text));
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
