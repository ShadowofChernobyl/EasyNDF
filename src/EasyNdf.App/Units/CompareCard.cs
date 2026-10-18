using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// One unit on Compare: a raised card across the list's width with three parts side by side. On the left who it is:
/// its picture, name, nation and role, whether it is the pinned unit, and Remove and ▾. In the middle its stats, each
/// with its value, how far it is from the pinned unit's, and a block bar with a mark where the pinned unit's reaches.
/// On the right its weapons. As the card gets narrower the weapons are left out, then the stats go under the rest,
/// so the card never runs past the window's edge.
/// </summary>
public sealed class CompareCard : Grid
{
    /// <summary>The card is this wide at least to show its weapons beside its stats.</summary>
    private const double WidthForWeapons = 860;

    /// <summary>The card is this wide at least to show its stats beside its picture.</summary>
    private const double WidthForStatsBeside = 540;

    /// <summary>The identity column's width, and the stats column's.</summary>
    private const double IdentityWidth = 170, StatsWidth = 330;

    /// <summary>The blocks in a stat's bar.</summary>
    private const int MeterBlocks = 14;

    private readonly Grid parts = new();
    private readonly FrameworkElement identity, stats, weapons;

    /// <summary>Builds a unit's card.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="pinned">The pinned unit, which the others are measured from; it may be this one.</param>
    /// <param name="best">Each stat's best value among the units compared, by its key; null where there is none.</param>
    /// <param name="bestRange">The longest weapon range among the units compared.</param>
    /// <param name="actions">What the card's buttons and menus do.</param>
    /// <param name="remove">What Remove does.</param>
    public CompareCard(Unit unit, Unit pinned, IReadOnlyDictionary<string, double?> best, double? bestRange, IUnitActions actions, Action remove)
    {
        Unit = unit;
        bool isPinned = ReferenceEquals(unit, pinned);
        identity = Identity(unit, pinned, isPinned, actions, remove);
        stats = Section(Texts.Get("Units.Compare.Stats"), null, Stats(unit, isPinned ? null : pinned, best));
        weapons = Section(Texts.Get("Units.Weapons"), unit.Weapons.Count,
            WeaponLedger.Build(unit, actions.Game.RangeUnit, narrow: false, bestRange, (weapon, at) => actions.OpenWeaponMenu(unit, weapon, at)));
        parts.Children.Add(identity);
        parts.Children.Add(stats);
        parts.Children.Add(weapons);

        var card = new Bevel { Child = parts, Padding = new Thickness(8) };
        card.SetResourceReference(Bevel.TopLeftProperty, "hi");
        card.SetResourceReference(Bevel.BottomRightProperty, "lo");
        card.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Children.Add(card);
        // the pinned unit's card is outlined in the accent colour, 1 pixel inside its edge
        if (isPinned)
        {
            var outline = new System.Windows.Shapes.Rectangle { StrokeThickness = 1, Margin = new Thickness(1), IsHitTestVisible = false };
            outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "accent");
            Children.Add(outline);
        }
        LayOut(Layout.Wide);
        SizeChanged += (_, e) => LayOut(LayoutFor(e.NewSize.Width));
        MouseRightButtonUp += (_, e) => { e.Handled = true; actions.OpenUnitMenu(unit, this); };
    }

    /// <summary>The unit on the card.</summary>
    internal Unit Unit { get; }

    /// <summary>Whether the card shows its weapons, which it leaves out when narrow.</summary>
    internal bool ShowsWeapons => weapons.Visibility == Visibility.Visible;

    /// <summary>The card's three ways of laying out its parts, widest first.</summary>
    private enum Layout { Wide, StatsBeside, StatsBelow }

    private Layout shown = (Layout)(-1);

    private static Layout LayoutFor(double width) =>
        width >= WidthForWeapons ? Layout.Wide : width >= WidthForStatsBeside ? Layout.StatsBeside : Layout.StatsBelow;

    /// <summary>Puts the parts in their columns: identity, stats and weapons side by side, 14 pixels apart; or fewer.</summary>
    private void LayOut(Layout layout)
    {
        if (layout == shown) return;
        shown = layout;
        parts.ColumnDefinitions.Clear();
        parts.RowDefinitions.Clear();
        weapons.Visibility = layout == Layout.Wide ? Visibility.Visible : Visibility.Collapsed;
        if (layout == Layout.StatsBelow)
        {
            parts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(identity, 0, 0);
            Place(stats, 1, 0);
            identity.MaxWidth = IdentityWidth;
            identity.HorizontalAlignment = HorizontalAlignment.Left;
            stats.Margin = new Thickness(0, 8, 0, 0);
            return;
        }
        identity.MaxWidth = double.PositiveInfinity;
        identity.HorizontalAlignment = HorizontalAlignment.Stretch;
        stats.Margin = new Thickness(14, 0, 0, 0);
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IdentityWidth) });
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = layout == Layout.Wide ? new GridLength(StatsWidth + 14) : new GridLength(1, GridUnitType.Star) });
        Place(identity, 0, 0);
        Place(stats, 0, 1);
        if (layout != Layout.Wide) return;
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        weapons.Margin = new Thickness(14, 0, 0, 0);
        Place(weapons, 0, 2);
    }

    private static void Place(FrameworkElement part, int row, int column)
    {
        SetRow(part, row);
        SetColumn(part, column);
    }

    /// <summary>
    /// Who the unit is: its picture, its name (14 pixels, bold), its nation's mark and name and its role; then
    /// "Pinned" on the pinned unit, or which unit it is compared to; then Remove and ▾.
    /// </summary>
    private static StackPanel Identity(Unit unit, Unit pinned, bool isPinned, IUnitActions actions, Action remove)
    {
        var name = new TextBlock { Text = unit.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
        var what = Small(UnitNames.Nation(unit.Nation) + " · " + UnitNames.RoleOf(unit));
        what.Margin = new Thickness(5, 0, 0, 0);
        var nationLine = new DockPanel { Children = { new NationMark(unit.Nation, actions.IsFirstSide(unit)), what } };

        TextBlock comparedTo;
        if (isPinned)
        {
            comparedTo = new TextBlock { Text = Texts.Get("Units.Compare.Pinned"), FontSize = 11, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            comparedTo.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        }
        else comparedTo = Small(Texts.Get("Units.Compare.ComparedTo", ("unit", pinned.Name)));
        comparedTo.Margin = new Thickness(0, 2, 0, 0);

        var removeButton = SmallButton(Texts.Get("Units.Compare.Remove"), Texts.Get("Units.Compare.Remove.Tip"));
        removeButton.Click += (_, _) => remove();
        var moreButton = SmallButton("▾", Texts.Get("Units.Compare.More.Tip"));
        moreButton.Click += (_, _) => actions.OpenUnitMenu(unit, moreButton);
        var buttons = new WrapRow { Margin = new Thickness(0, 6, 0, 0), Children = { removeButton, moreButton } };

        return new StackPanel { Children = { new UnitPicture { Source = unit.PictureSource, Factory = unit.Factory }, name, nationLine, comparedTo, buttons } };
    }

    /// <summary>A small button: 20 pixels high, 7 pixels each side, 11.5-pixel text.</summary>
    private static Button SmallButton(string text, string tip) =>
        new() { Content = text, ToolTip = tip, MinHeight = 20, Padding = new Thickness(7, 0, 7, 0), FontSize = 11.5 };

    /// <summary>
    /// The stats, one a row: the name, dim; the value, bold in the "best" colour when it is the best; how far it is from
    /// the pinned unit's (better in "ok", worse in "warn"); and its bar, with the pinned unit's mark.
    /// </summary>
    private static StackPanel Stats(Unit unit, Unit? pinned, IReadOnlyDictionary<string, double?> best)
    {
        var rows = new StackPanel();
        foreach (UnitStat stat in UnitStat.All)
        {
            double? value = stat.Read(unit), pinnedValue = pinned is null ? null : stat.Read(pinned);
            bool isBest = value is not null && value == best.GetValueOrDefault(stat.Key);
            var row = new Grid();
            foreach (GridLength width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(52), new GridLength(52), GridLength.Auto })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

            var label = Small(UnitNames.Stat(stat.Key));
            label.FontSize = 11.5;
            // on one line, so every row keeps the same height; cut short with "..." when the card is narrow
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.ToolTip = UnitNames.Stat(stat.Key);
            var number = new TextBlock { Text = UnitNumbers.Of(value), TextAlignment = TextAlignment.Right, FontSize = 12, FontWeight = isBest ? FontWeights.Bold : FontWeights.Normal };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            number.SetResourceReference(TextBlock.ForegroundProperty, isBest ? "ok" : "ink");
            var difference = new TextBlock { Text = UnitNumbers.Difference(value, pinnedValue), TextAlignment = TextAlignment.Right, FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
            difference.SetResourceReference(TextBlock.FontFamilyProperty, "mono");
            difference.SetResourceReference(TextBlock.ForegroundProperty, FeelColour(stat, value, pinnedValue));
            var meter = new BlockMeter { Value = value, FullBar = stat.FullBar, Blocks = MeterBlocks, IsBest = isBest, Marker = pinnedValue, Margin = new Thickness(6, 0, 0, 0) };
            if (pinned is not null && pinnedValue is not null) meter.ToolTip = pinned.Name + ": " + UnitNumbers.Of(pinnedValue);

            SetColumn(number, 1);
            SetColumn(difference, 2);
            SetColumn(meter, 3);
            foreach (UIElement cell in new UIElement[] { label, number, difference, meter }) row.Children.Add(cell);
            var line = new Bevel { Child = row, Padding = new Thickness(0, 2, 0, 2), Edges = new Thickness(0, 0, 0, 1) };
            line.SetResourceReference(Bevel.BottomRightProperty, "line");
            rows.Children.Add(line);
        }
        return rows;
    }

    /// <summary>The colour of a difference: "ok" when better than the pinned unit's, "warn" when worse, dim when the same.</summary>
    private static string FeelColour(UnitStat stat, double? value, double? pinnedValue)
    {
        if (value is not double own || pinnedValue is not double other) return "muted";
        return stat.Feel(own - other) switch { 1 => "ok", -1 => "warn", _ => "muted" };
    }

    /// <summary>A part's heading: bold, 12 pixels, in the accent colour, with a dim count after it.</summary>
    private static StackPanel Section(string title, int? count, FrameworkElement content)
    {
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "accent");
        if (count is int number)
        {
            var countText = new Run("  " + number.ToString(Texts.Catalog.Culture)) { FontWeight = FontWeights.Normal, FontSize = 11 };
            countText.SetResourceReference(TextElement.ForegroundProperty, "muted");
            heading.Inlines.Add(countText);
        }
        return new StackPanel { Children = { heading, content } };
    }

    private static TextBlock Small(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }
}
