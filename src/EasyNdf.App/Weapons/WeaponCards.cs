using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>A row of weapon cards in the Cards view; the list makes only the rows in view.</summary>
/// <param name="Weapons">The weapons on the row, left to right.</param>
/// <param name="Columns">How many cards fit across, so a short last row keeps the others' card width.</param>
/// <param name="IsLast">Whether it is the last row, which keeps the gap under it.</param>
public sealed record WeaponCardRow(IReadOnlyList<Weapon> Weapons, int Columns, bool IsLast);

/// <summary>
/// One weapon as a card on the Weapons sub-tab, as the mock's: a line with its damage level at the left and how many
/// units carry it at the right, its picture under that, then its name, then its kind at the left and its traits'
/// pictures at the right. The words are never drawn over the picture. Raised, or sunk on the selection colour when
/// selected, as a unit's tile is.
/// </summary>
public sealed class WeaponCard : Bevel
{
    private readonly WeaponPicture picture = new() { Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock level = new() { FontWeight = FontWeights.Bold, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock users = new() { FontSize = 10.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock name = new() { FontWeight = FontWeights.Bold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock kind = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl traits = new() { Focusable = false, Margin = new Thickness(4, 0, 0, 0) };

    /// <summary>Builds an empty card; <see cref="Show"/> fills it.</summary>
    public WeaponCard()
    {
        Padding = new Thickness(5);
        Cursor = Cursors.Hand;
        level.SetResourceReference(TextBlock.ForegroundProperty, "caution");
        users.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        kind.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(users, Dock.Right);
        DockPanel.SetDock(traits, Dock.Right);
        var top = new DockPanel { Children = { users, level } };
        var bottom = new DockPanel { Children = { traits, kind } };
        Child = new StackPanel { Children = { top, picture, name, bottom } };
    }

    /// <summary>The weapon shown.</summary>
    public Weapon? Weapon { get; private set; }

    /// <summary>Shows a weapon, selected or not.</summary>
    public void Show(Weapon weapon, bool isSelected)
    {
        Weapon = weapon;
        picture.Source = weapon.PictureSource;
        // a value the open mod changes is marked, as everywhere in the Armory; a line with no level keeps its height
        string levelText = weapon.DamageLevel > 0 ? WeaponNames.Damage(weapon) : " ";
        WeaponMarks.Show(level, weapon, WeaponField.DamageLevel, levelText, WeaponNames.Damage);
        WeaponMarks.Show(users, weapon, WeaponField.Users, Texts.Count("Weapons.UnitCount", weapon.Users.Count), each => Texts.Count("Weapons.UnitCount", each.Users.Count));
        WeaponMarks.Show(name, weapon, WeaponField.Name, weapon.Name, each => each.Name);
        name.ToolTip ??= weapon.Name;
        kind.Text = WeaponNames.KindAndDamage(weapon);
        traits.Content = TraitIcon.RowFor(weapon, 14);
        SetResourceReference(TopLeftProperty, isSelected ? "lo" : "hi");
        SetResourceReference(BottomRightProperty, isSelected ? "hi" : "lo");
        SetResourceReference(BackgroundProperty, isSelected ? "sel" : "panel");
    }
}

/// <summary>
/// A row of the Cards view: each card in an equal share of the width, as the units' tiles are (see
/// <see cref="Units.CardRowView"/>), the same gap between them and around them.
/// </summary>
public sealed class WeaponCardRowView : Grid
{
    /// <summary>The space between cards, and around them: the units' tiles' own.</summary>
    public const double Gap = Units.CardRowView.Gap;

    /// <summary>The narrowest a card is made before one fewer fits across.</summary>
    public const double SmallestCard = 160;

    /// <summary>The descriptor of the weapon selected, whose card shows sunk; set by the view before it draws its rows.</summary>
    public static string? Selected { get; set; }

    /// <summary>How many cards fit across a list this wide.</summary>
    public static int ColumnsFor(double width)
    {
        // the room inside the row's margins holds n cards and the n - 1 gaps between them
        double inside = width - 2 * Gap;
        return Math.Max(1, (int)((inside + Gap) / (SmallestCard + Gap)));
    }

    /// <summary>Builds the row for the line it is given as its data.</summary>
    public WeaponCardRowView()
    {
        DataContextChanged += (_, _) => Build();
    }

    /// <summary>One card for each weapon of the row, cards kept from the row's last use when there are enough.</summary>
    private void Build()
    {
        if (DataContext is not WeaponCardRow row) return;
        Margin = new Thickness(Gap, Gap, Gap, row.IsLast ? Gap : 0);
        if (ColumnDefinitions.Count != 2 * row.Columns - 1)
        {
            ColumnDefinitions.Clear();
            for (int column = 0; column < row.Columns; column++)
            {
                if (column > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
        }
        while (Children.Count > row.Weapons.Count) Children.RemoveAt(Children.Count - 1);
        while (Children.Count < row.Weapons.Count) Children.Add(new WeaponCard());
        for (int at = 0; at < row.Weapons.Count; at++)
        {
            var card = (WeaponCard)Children[at];
            SetColumn(card, at * 2);
            card.Show(row.Weapons[at], row.Weapons[at].Descriptor == Selected);
        }
    }
}
