using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// One weapon on the Weapons sub-tab's Compare, the twin of a unit's card there (<see cref="CompareCard"/>): a raised
/// card across the list's width. On the left who it is: its picture, name and sort, whether it is the pinned weapon,
/// and Remove and ▾. On the right its values, each with how far it is from the pinned weapon's (better in "ok", worse
/// in "warn") and a block bar with a mark where the pinned weapon's reaches; the best value of the weapons compared
/// in the "ok" colour. Narrow, the values go under the rest.
/// </summary>
public sealed class WeaponCompareCard : Grid
{
    /// <summary>A value compared: its name's text key, its field (for the marks), how it is read and written, and whether more is better.</summary>
    public sealed record ComparedValue(string NameKey, string Field, Func<Weapon, double?> Read, Func<Weapon, string> Write, bool MoreIsBetter);

    /// <summary>The values compared, in the order the Weapon pane shows them.</summary>
    public static IReadOnlyList<ComparedValue> Values { get; } =
    [
        new("Weapons.Pane.Level", WeaponField.DamageLevel, weapon => weapon.DamageLevel > 0 ? weapon.DamageLevel : null,
            weapon => weapon.DamageLevel > 0 ? Texts.Number(weapon.DamageLevel) : UnitNumbers.None, MoreIsBetter: true),
        new("Weapons.Pane.Damage", WeaponField.Damage, weapon => weapon.Damage, weapon => WeaponNames.Number(weapon.Damage), MoreIsBetter: true),
        new("Weapons.Pane.Suppression", WeaponField.Suppression, weapon => weapon.Suppression, weapon => WeaponNames.Number(weapon.Suppression), MoreIsBetter: true),
        new("Weapons.Pane.DamageRadius", WeaponField.DamageRadius, weapon => weapon.DamageRadius, weapon => WeaponNames.Number(weapon.DamageRadius), MoreIsBetter: true),
        new("Weapons.Pane.Ground", WeaponField.Range, weapon => weapon.Range, weapon => WeaponNames.Range(weapon.Range), MoreIsBetter: true),
        new("Weapons.Pane.Helicopters", WeaponField.HelicopterRange, weapon => weapon.HelicopterRange, weapon => WeaponNames.Range(weapon.HelicopterRange), MoreIsBetter: true),
        new("Weapons.Pane.Planes", WeaponField.PlaneRange, weapon => weapon.PlaneRange, weapon => WeaponNames.Range(weapon.PlaneRange), MoreIsBetter: true),
        new("Weapons.Pane.Minimum", WeaponField.MinimumRange, weapon => weapon.MinimumRange, weapon => WeaponNames.Range(weapon.MinimumRange), MoreIsBetter: false),
        new("Weapons.Pane.Stopped", WeaponField.AccuracyStopped, weapon => weapon.AccuracyStopped, weapon => WeaponNames.Percent(weapon.AccuracyStopped), MoreIsBetter: true),
        new("Weapons.Pane.Moving", WeaponField.AccuracyMoving, weapon => weapon.AccuracyMoving, weapon => WeaponNames.Percent(weapon.AccuracyMoving), MoreIsBetter: true),
        new("Weapons.Pane.DispersionFar", WeaponField.DispersionFar, weapon => weapon.DispersionFar, weapon => WeaponNames.Number(weapon.DispersionFar), MoreIsBetter: false),
        new("Weapons.Pane.Aiming", WeaponField.AimingTime, weapon => weapon.AimingTime, weapon => WeaponNames.Seconds(weapon.AimingTime), MoreIsBetter: false),
        new("Weapons.Pane.Reload", WeaponField.Reload, weapon => weapon.Reload, weapon => WeaponNames.Seconds(weapon.Reload), MoreIsBetter: false),
        new("Weapons.Pane.PerSalvo", WeaponField.ShotsPerSalvo, weapon => weapon.ShotsPerSalvo, weapon => WeaponNames.Number(weapon.ShotsPerSalvo), MoreIsBetter: true),
        new("Weapons.Pane.Supply", WeaponField.SupplyCost, weapon => weapon.SupplyCost, weapon => WeaponNames.Number(weapon.SupplyCost), MoreIsBetter: false),
    ];

    /// <summary>The card is this wide at least to show its values beside who it is.</summary>
    private const double WidthForValuesBeside = 540;

    /// <summary>The identity column's width.</summary>
    private const double IdentityWidth = 170;

    /// <summary>The blocks in a value's bar, as on the units' Compare.</summary>
    private const int MeterBlocks = 14;

    private readonly Grid parts = new();
    private readonly FrameworkElement identity, values;
    private bool? valuesBeside;

    /// <summary>Builds a weapon's card.</summary>
    /// <param name="weapon">The weapon.</param>
    /// <param name="pinned">The pinned weapon, which the others are measured from; it may be this one.</param>
    /// <param name="compared">Every weapon compared, for each value's best and its bar's full length.</param>
    /// <param name="remove">What Remove does.</param>
    /// <param name="openMenu">Opens the weapon's menu, under ▾ or where the card is right-clicked.</param>
    public WeaponCompareCard(Weapon weapon, Weapon pinned, IReadOnlyList<Weapon> compared, Action remove, Action<FrameworkElement> openMenu)
    {
        Weapon = weapon;
        bool isPinned = weapon.Descriptor == pinned.Descriptor;
        identity = Identity(weapon, pinned, isPinned, remove, openMenu);
        values = ValueRows(weapon, isPinned ? null : pinned, compared);
        parts.Children.Add(identity);
        parts.Children.Add(values);

        var card = new Bevel { Child = parts, Padding = new Thickness(8) };
        card.SetResourceReference(Bevel.TopLeftProperty, "hi");
        card.SetResourceReference(Bevel.BottomRightProperty, "lo");
        card.SetResourceReference(Bevel.BackgroundProperty, "panel");
        Children.Add(card);
        // the pinned weapon's card is outlined in the accent colour, 1 pixel inside its edge, as the pinned unit's is
        if (isPinned)
        {
            var outline = new System.Windows.Shapes.Rectangle { StrokeThickness = 1, Margin = new Thickness(1), IsHitTestVisible = false };
            outline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "accent");
            Children.Add(outline);
        }
        LayOut(beside: true);
        SizeChanged += (_, e) => LayOut(e.NewSize.Width >= WidthForValuesBeside);
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            openMenu(this);
        };
    }

    /// <summary>The weapon on the card.</summary>
    internal Weapon Weapon { get; }

    /// <summary>Who the weapon is and its values side by side, 14 pixels apart; or, narrow, the values under it.</summary>
    private void LayOut(bool beside)
    {
        if (beside == valuesBeside) return;
        valuesBeside = beside;
        parts.ColumnDefinitions.Clear();
        parts.RowDefinitions.Clear();
        if (!beside)
        {
            parts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            parts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(identity, 0, 0);
            Place(values, 1, 0);
            identity.MaxWidth = IdentityWidth;
            identity.HorizontalAlignment = HorizontalAlignment.Left;
            values.Margin = new Thickness(0, 8, 0, 0);
            return;
        }
        identity.MaxWidth = double.PositiveInfinity;
        identity.HorizontalAlignment = HorizontalAlignment.Stretch;
        values.Margin = new Thickness(14, 0, 0, 0);
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IdentityWidth) });
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(identity, 0, 0);
        Place(values, 0, 1);
    }

    private static void Place(FrameworkElement part, int row, int column)
    {
        SetRow(part, row);
        SetColumn(part, column);
    }

    /// <summary>
    /// Who the weapon is: its picture, its name (14 pixels, bold), its sort and damage; "Pinned" on the pinned weapon,
    /// or which weapon it is compared to; then Remove and ▾.
    /// </summary>
    private static StackPanel Identity(Weapon weapon, Weapon pinned, bool isPinned, Action remove, Action<FrameworkElement> openMenu)
    {
        var name = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
        WeaponMarks.Show(name, weapon, WeaponField.Name, weapon.Name, each => each.Name);
        TextBlock what = Small(WeaponNames.KindAndDamage(weapon) + " · " + Texts.Count("Weapons.UnitCount", weapon.Users.Count));

        TextBlock comparedTo;
        if (isPinned)
        {
            comparedTo = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            comparedTo.SetResourceReference(TextBlock.ForegroundProperty, "accent");
            // the pushpin emoji, in colour, before the words, as on the units' Compare
            var pin = new Image { Source = (System.Windows.Media.ImageSource)Application.Current.FindResource("PushpinEmoji"), Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, -2) };
            comparedTo.Inlines.Add(new System.Windows.Documents.InlineUIContainer(pin) { BaselineAlignment = BaselineAlignment.Center });
            comparedTo.Inlines.Add(new System.Windows.Documents.Run(Texts.Get("Weapons.Compare.Pinned")));
        }
        else comparedTo = Small(Texts.Get("Units.Compare.ComparedTo", ("unit", pinned.Name)));
        comparedTo.Margin = new Thickness(0, 2, 0, 0);

        var removeButton = SmallButton(Texts.Get("Units.Compare.Remove"), Texts.Get("Weapons.Compare.Remove.Tip"));
        removeButton.Click += (_, _) => remove();
        var moreButton = SmallButton("▾", Texts.Get("Weapons.Compare.More.Tip"));
        moreButton.Click += (_, _) => openMenu(moreButton);
        var buttons = new WrapRow { Margin = new Thickness(0, 6, 0, 0), Children = { removeButton, moreButton } };

        return new StackPanel { Children = { new WeaponPicture { Source = weapon.PictureSource }, name, what, comparedTo, buttons } };
    }

    /// <summary>A small button: 20 pixels high, 7 pixels each side, 11.5-pixel text, as on the units' Compare.</summary>
    private static Button SmallButton(string text, string tip) =>
        new() { Content = text, ToolTip = tip, MinHeight = 20, Padding = new Thickness(7, 0, 7, 0), FontSize = 11.5 };

    /// <summary>
    /// The values, one a row: the name, dim; the value, in the "ok" colour when it is the best of the weapons compared;
    /// how far it is from the pinned weapon's; and its bar, with the pinned weapon's mark.
    /// </summary>
    private static StackPanel ValueRows(Weapon weapon, Weapon? pinned, IReadOnlyList<Weapon> compared)
    {
        var rows = new StackPanel();
        foreach (ComparedValue compare in Values)
        {
            double? value = compare.Read(weapon), pinnedValue = pinned is null ? null : compare.Read(pinned);
            List<double> all = [.. compared.Select(compare.Read).OfType<double>()];
            double? best = all.Count == 0 ? null : compare.MoreIsBetter ? all.Max() : all.Min();
            bool isBest = value is not null && value == best && all.Count > 1;
            var row = new Grid();
            foreach (GridLength width in new[] { new GridLength(1, GridUnitType.Star), new GridLength(64), new GridLength(56), GridLength.Auto })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

            TextBlock label = Small(Texts.Get(compare.NameKey));
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var number = new TextBlock { TextAlignment = TextAlignment.Right, FontSize = 12 };
            WeaponMarks.Show(number, weapon, compare.Field, compare.Write(weapon), compare.Write);
            number.SetResourceReference(TextBlock.ForegroundProperty, isBest ? "ok" : "ink");
            var difference = new TextBlock { Text = UnitNumbers.Difference(value, pinnedValue), TextAlignment = TextAlignment.Right, FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
            difference.SetResourceReference(TextBlock.ForegroundProperty, FeelColour(compare, value, pinnedValue));
            // a full bar is the largest of the weapons compared, so they can be told apart whatever their size
            double fullBar = all.Count == 0 ? 1 : Math.Max(all.Max(), double.Epsilon);
            var meter = new BlockMeter { Value = value, FullBar = fullBar, Blocks = MeterBlocks, IsBest = isBest, Marker = pinnedValue, Margin = new Thickness(6, 0, 0, 0) };
            if (pinned is not null && pinnedValue is not null) meter.ToolTip = pinned.Name + ": " + compare.Write(pinned);

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

    /// <summary>The colour of a difference: "ok" when better than the pinned weapon's, "warn" when worse, dim when the same.</summary>
    private static string FeelColour(ComparedValue compare, double? value, double? pinnedValue)
    {
        if (value is not double own || pinnedValue is not double other || own == other) return "muted";
        return (own > other) == compare.MoreIsBetter ? "ok" : "warn";
    }

    private static TextBlock Small(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        return block;
    }
}
