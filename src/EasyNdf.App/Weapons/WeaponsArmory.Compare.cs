using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Weapons;

/// <summary>
/// The Weapons sub-tab's Compare, as the Units sub-tab's: a card a weapon (<see cref="WeaponCompareCard"/>), the
/// pinned weapon first and the others measured from it. Weapons are added from their menu (a right-click) or the
/// Weapon pane's Compare button, and kept by descriptor, so they stay when the weapons are read again.
/// </summary>
public partial class WeaponsArmory
{
    /// <summary>The weapons on Compare, by descriptor, in the order they were added.</summary>
    private readonly List<string> compared = [];

    /// <summary>The weapon the others on Compare are measured from: the first added, until another is pinned.</summary>
    private string? pinned;

    /// <summary>Adds a weapon to Compare, after those there, and shows Compare.</summary>
    private void Compare(Weapon weapon)
    {
        if (!compared.Contains(weapon.Descriptor)) compared.Add(weapon.Descriptor);
        ShowView(WeaponView.Compare);
    }

    private void RemoveFromCompare(Weapon weapon)
    {
        compared.Remove(weapon.Descriptor);
        ShowAll();
    }

    /// <summary>"Pin for Comparison": the others on Compare are measured from this weapon, which goes first.</summary>
    private void Pin(Weapon weapon)
    {
        pinned = weapon.Descriptor;
        ShowAll();
    }

    /// <summary>
    /// Compare: a card a weapon, the pinned weapon first and the others in the order they were added. A weapon no
    /// longer read (a copy taken out of the mod) is left out.
    /// </summary>
    private void ShowCompare()
    {
        CompareList.Children.Clear();
        compared.RemoveAll(descriptor => !AllWeapons.Any(weapon => weapon.Descriptor == descriptor));
        List<Weapon> weapons = [.. compared.Select(descriptor => AllWeapons.First(weapon => weapon.Descriptor == descriptor))];
        if (pinned is null || !compared.Contains(pinned)) pinned = compared.FirstOrDefault();
        Weapon? pinnedWeapon = weapons.FirstOrDefault(weapon => weapon.Descriptor == pinned);
        if (pinnedWeapon is null)
        {
            var empty = new TextBlock { Text = Texts.Get("Weapons.Compare.Nothing"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 10) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "muted");
            CompareList.Children.Add(empty);
            return;
        }
        foreach (Weapon weapon in weapons.OrderBy(weapon => weapon.Descriptor == pinned ? 0 : 1))
        {
            Weapon shownWeapon = weapon;
            CompareList.Children.Add(new WeaponCompareCard(weapon, pinnedWeapon, weapons, () => RemoveFromCompare(shownWeapon), at => OpenWeaponMenu(shownWeapon, at))
            {
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }

    /// <summary>
    /// A weapon's menu, on a right-click on its card or row, or ▾ on Compare: show it in the File Editor, make a rule
    /// for it, compare it (or pin it, or take it off Compare), copy its descriptor.
    /// </summary>
    private void OpenWeaponMenu(Weapon weapon, FrameworkElement at)
    {
        if (actions is null) return;
        IWeaponActions tab = actions;
        bool onCompare = view == WeaponView.Compare && compared.Contains(weapon.Descriptor);
        MenuChoice?[] items =
        [
            Menus.Heading(weapon.Name),
            new MenuChoice(Texts.Get("Units.Pane.ShowInEditor"), () => tab.ShowInEditor(weapon), IsEnabled: tab.CanShowInEditor(weapon)),
            new MenuChoice(Texts.Get("Weapons.Menu.NewRule"), () => tab.NewRuleFor(weapon)),
            Menus.Line,
            onCompare && weapon.Descriptor != pinned ? new MenuChoice(Texts.Get("Units.Menu.Pin"), () => Pin(weapon)) : null,
            onCompare ? new MenuChoice(Texts.Get("Weapons.Menu.RemoveFromCompare"), () => RemoveFromCompare(weapon))
                : new MenuChoice(Texts.Get("Units.Menu.Compare"), () => Compare(weapon), IsEnabled: !compared.Contains(weapon.Descriptor)),
            new MenuChoice(Texts.Get("Units.Menu.CopyName"), () => Pages.UnitsPage.Copy(weapon.Descriptor), Key: weapon.Descriptor),
            tab.IsCopy(weapon) ? new MenuChoice(Texts.Get("Weapons.Menu.Delete"), () => _ = tab.DeleteWeapon(weapon), Key: "Delete") : null,
        ];
        if (at is Button) Menus.ShowBelow(at, items);
        else Menus.Show(at, items);
    }
}
