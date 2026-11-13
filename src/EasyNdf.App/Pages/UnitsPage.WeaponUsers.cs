using System.Windows;
using System.Windows.Input;
using EasyNdf.App.Text;
using EasyNdf.App.Weapons;
using EasyNdf.Data;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// Show Units Using This Weapon, from a weapon's menu or the Weapon pane: the Units sub-tab lists only the units that
/// carry the weapon, which its heading names. The other filters clear first, so none of those units are hidden, and can
/// then narrow them further; ✕ shows every unit, with the filters as they were. It stays on over sub-tab switches, until
/// ✕ or until another source is read.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The filters a weapon narrowed the units from, which ✕ puts back; the same shape clears them.</summary>
    private sealed record UnitFilters(string Text, string Side, string[] Nations, string Role, bool OnlyInDivision, bool OnlyModded, string Factory);

    /// <summary>The descriptor of the weapon whose units the Units sub-tab lists; null for every unit.</summary>
    private string? usedWeapon;

    /// <summary>The filters as they were before <see cref="usedWeapon"/>; null when none is chosen.</summary>
    private UnitFilters? filtersBeforeWeapon;

    /// <inheritdoc cref="Units.IUnitActions.MarkedWeapon"/>
    public string? MarkedWeapon => part == ArmoryPart.Units ? usedWeapon : null;

    /// <inheritdoc/>
    public void ShowUnitsUsing(Weapon weapon)
    {
        if (weapon.Users.Count == 0) return;
        // from one weapon to another, ✕ still goes back to the filters from before the first
        filtersBeforeWeapon ??= new UnitFilters(filter.Text, filter.Side, [.. filter.Nations], filter.Role, filter.OnlyInDivision, filter.OnlyModded, filter.Factory);
        usedWeapon = weapon.Descriptor;
        SetFilters(new UnitFilters("", "", [], "", false, false, ""));
        // Compare lists the units put on it, not the filters'
        if (view == UnitView.Compare) view = RememberedView;
        // as a division chosen does: the pane does not go on showing a unit the list has not got
        if (selection.Selected is Data.Units.Unit unit && !weapon.Users.Contains(unit.Descriptor)) selection.Selected = null;
        ScrollUnitsToTop();
        ShownThingsChanged();
        ShowPart(ArmoryPart.Units);
    }

    /// <summary>
    /// The filter's units, as the weapon has them now, and the heading's mark. A weapon no longer read (a copy taken out
    /// of the mod) lets go. Only the Units sub-tab is narrowed.
    /// </summary>
    private void NarrowToUsedWeapon()
    {
        Weapon? weapon = usedWeapon is null ? null : loaded?.Catalog.Weapons?.FirstOrDefault(each => each.Descriptor == usedWeapon);
        if (usedWeapon is not null && weapon is null) LetGoOfUsedWeapon();
        bool narrowed = part == ArmoryPart.Units && weapon is not null;
        filter.Carrying = narrowed ? new HashSet<string>(weapon!.Users, StringComparer.Ordinal) : null;
        UsedWeaponMark.Visibility = narrowed ? Visibility.Visible : Visibility.Collapsed;
        if (!narrowed) return;
        UsedWeaponName.Text = weapon!.Name;
        UsedWeaponName.ToolTip = Texts.Get("Units.UsingWeapon.Back.Tip", ("name", weapon.Name));
        System.Windows.Automation.AutomationProperties.SetName(UsedWeaponName, weapon.Name);
    }

    /// <summary>The heading's ✕: every unit again, with the filters as they were.</summary>
    private void ClearUsedWeaponClicked(object sender, RoutedEventArgs e)
    {
        LetGoOfUsedWeapon();
        ScrollUnitsToTop();
        ShowAll();
    }

    /// <summary>A weapon's units, or every unit again, are another list: it starts at its top, not where the last was scrolled to.</summary>
    private void ScrollUnitsToTop()
    {
        WeaponsArmory.VisualsIn<Controls.LinesPanel>(CardsList).FirstOrDefault()?.SetVerticalOffset(0);
        if (TableList.UnitsInOrder.Count > 0) TableList.ScrollToUnit(TableList.UnitsInOrder[0]);
    }

    /// <summary>The weapon's name in the heading is clicked: it is shown on the Weapons sub-tab; its units stay listed here.</summary>
    private void UsedWeaponNameClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowUsedWeapon();
    }

    private void ShowUsedWeapon()
    {
        if (usedWeapon is not null) ShowWeaponInArmory(usedWeapon);
    }

    /// <summary>No weapon narrows the units any more: the filters are as they were before it.</summary>
    private void LetGoOfUsedWeapon()
    {
        usedWeapon = null;
        if (filtersBeforeWeapon is UnitFilters before) SetFilters(before);
        filtersBeforeWeapon = null;
    }

    /// <summary>Sets the filters and the controls that show them, without drawing anything.</summary>
    private void SetFilters(UnitFilters filters)
    {
        filter.Text = filters.Text;
        filter.Side = filters.Side;
        filter.Nations.Clear();
        filter.Nations.UnionWith(filters.Nations);
        filter.Role = filters.Role;
        filter.OnlyInDivision = filters.OnlyInDivision;
        filter.OnlyModded = filters.OnlyModded;
        filter.Factory = filters.Factory;
        SearchBox.Text = filters.Text;
        // the search is set already: the box's change would draw everything again a moment later
        searchPause.Stop();
        OnlyInDivision.IsChecked = filters.OnlyInDivision;
        OnlyModded.IsChecked = filters.OnlyModded;
        FillSideChoices();
        // Tuned for speed: the role list's rows are the same, so only its choice changes
        ShowRoleChosen();
    }
}
