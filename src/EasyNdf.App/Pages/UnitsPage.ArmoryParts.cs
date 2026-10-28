using System.Windows;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Pages;

/// <summary>
/// The Armory's sub-tabs: Units (every unit of the source), Divisions (a division's units, card slots and details),
/// Battlegroups (a division's Official Battlegroup) and Weapons (every weapon the units carry). Units, Divisions and
/// Battlegroups share the division list and the division chosen in it; on Units, a division chosen narrows the units
/// to its own, and ✕ shows every unit again.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The sub-tab shown.</summary>
    private ArmoryPart part = ArmoryPart.Units;

    /// <summary>Whether the centre shows a division's units: the Divisions sub-tab, with a division chosen.</summary>
    private bool ShowsDivisionUnits => part == ArmoryPart.Divisions && chosenDivision is not null;

    /// <summary>Whether the Units sub-tab shows only a division's units: one is chosen in its division list.</summary>
    private bool UnitsShowOneDivision => part == ArmoryPart.Units && chosenDivision is not null;

    /// <summary>Whether the centre shows a division's Official Battlegroup: the Battlegroups sub-tab, with a division chosen.</summary>
    private bool ShowsBattlegroup => part == ArmoryPart.Battlegroups && chosenDivision is not null;

    /// <summary>The sub-tab shown last time; Units the first time.</summary>
    private void PrepareParts()
    {
        part = Enum.TryParse((string?)AppData.Current.State["armoryPart"], out ArmoryPart remembered) ? remembered : ArmoryPart.Units;
        BattlegroupScroller.SizeChanged += BattlegroupResized;
        ShowPartButtons();
    }

    private void PartClicked(object sender, RoutedEventArgs e)
    {
        ArmoryPart clicked = sender == DivisionsPartButton ? ArmoryPart.Divisions
            : sender == BattlegroupsPartButton ? ArmoryPart.Battlegroups
            : sender == WeaponsPartButton ? ArmoryPart.Weapons
            : ArmoryPart.Units;
        ShowPart(clicked);
    }

    /// <summary>
    /// Shows a sub-tab, remembered for next time. Units shows every unit; Divisions and Battlegroups the division chosen
    /// last, or the first listed when none was; Weapons every weapon, in the units' place.
    /// </summary>
    private void ShowPart(ArmoryPart shown)
    {
        part = shown;
        AppData.Current.State["armoryPart"] = shown.ToString();
        AppData.Current.SaveState();
        if (shown == ArmoryPart.Weapons)
        {
            ShowPartButtons();
            ShowWeapons();
            return;
        }
        if (shown == ArmoryPart.Units) chosenDivision = null;
        else if (chosenDivision is null)
        {
            string? remembered = (string?)AppData.Current.State["division"];
            chosenDivision = divisions.FirstOrDefault(division => division.Descriptor == remembered) ?? FirstListedDivision();
            filter.Factory = "";
        }
        ShowPartButtons();
        ShowAll(onlyPartChanged: true);
    }

    /// <summary>
    /// The division Divisions and Battlegroups show when none is chosen (the one chosen was deleted, or none was ever
    /// chosen): the first one the list shows; null when there is none.
    /// </summary>
    private Division? FirstListedDivision() =>
        divisions.Where(division => filter.Side == "" || division.Side == filter.Side).OrderBy(division => IsNewInMod(division) ? 0 : 1).FirstOrDefault()
        ?? divisions.FirstOrDefault();

    /// <summary>Divisions and Battlegroups always show a division: one chosen anew when the one shown went.</summary>
    private void KeepADivisionChosen()
    {
        if (part is ArmoryPart.Divisions or ArmoryPart.Battlegroups && chosenDivision is null) chosenDivision = FirstListedDivision();
    }

    /// <summary>
    /// The sub-tab shown stays pressed; the left column has the division list on Units, Divisions and Battlegroups,
    /// the "in a division" filter on Units only (a division's units are all in one), and "Only modded units" on all but Battlegroups.
    /// </summary>
    private void ShowPartButtons()
    {
        UnitsPartButton.IsChecked = part == ArmoryPart.Units;
        DivisionsPartButton.IsChecked = part == ArmoryPart.Divisions;
        BattlegroupsPartButton.IsChecked = part == ArmoryPart.Battlegroups;
        WeaponsPartButton.IsChecked = part == ArmoryPart.Weapons;
        // the Weapons sub-tab has a left column and a pane of its own, in the units' place
        UnitsView.Visibility = part == ArmoryPart.Weapons ? Visibility.Collapsed : Visibility.Visible;
        WeaponsView.Visibility = part == ArmoryPart.Weapons ? Visibility.Visible : Visibility.Collapsed;
        OnlyInDivision.Visibility = part == ArmoryPart.Units ? Visibility.Visible : Visibility.Collapsed;
        // a battlegroup shows its cards whatever the filters
        OnlyModded.Visibility = part == ArmoryPart.Battlegroups ? Visibility.Collapsed : Visibility.Visible;
        // a new division is made from the Divisions sub-tab
        NewDivisionButton.Visibility = part == ArmoryPart.Divisions ? Visibility.Visible : Visibility.Collapsed;
        ShowListsSplit();
    }

    /// <summary>A division is chosen from elsewhere (a link, a new division): the Divisions sub-tab shows it, unless Battlegroups does.</summary>
    private void ShowPartForDivision(Division? division)
    {
        ArmoryPart wanted = division is null ? ArmoryPart.Units : part == ArmoryPart.Battlegroups ? ArmoryPart.Battlegroups : ArmoryPart.Divisions;
        if (wanted == part) return;
        part = wanted;
        AppData.Current.State["armoryPart"] = wanted.ToString();
        ShowPartButtons();
    }

    // ---- the links between the sub-tabs ----

    /// <summary>The Unit pane's divisions are links on this tab.</summary>
    public bool CanShowDivisions => true;

    /// <summary>
    /// A division named in the Unit pane is clicked: it is shown on the Divisions sub-tab. The pane names it as the
    /// source's files do, so a division the open mod renamed is found by the name it had.
    /// </summary>
    public void ShowDivisionCalled(string name)
    {
        string? descriptor = DivisionsBeforeArmoryChanges.FirstOrDefault(each => each.Name == name)?.Descriptor;
        Division? division = divisions.FirstOrDefault(each => each.Name == name) ?? divisions.FirstOrDefault(each => each.Descriptor == descriptor);
        if (division is null) return;
        part = ArmoryPart.Divisions;
        ChooseDivision(division);
        // chosen already, ChooseDivision did nothing: the sub-tab is shown all the same
        AppData.Current.State["armoryPart"] = part.ToString();
        ShowPartButtons();
        ShowAll();
    }

    /// <summary>The chosen division's Official Battlegroup, from the Division pane: shown on the Battlegroups sub-tab.</summary>
    public void ShowBattlegroup() => ShowPart(ArmoryPart.Battlegroups);

    /// <summary>The battlegroup's division, from its heading: shown on the Divisions sub-tab.</summary>
    private void ShowDivisionOfBattlegroup() => ShowPart(ArmoryPart.Divisions);

    /// <summary>
    /// Show in Units, from the Unit pane: the Units sub-tab, on the unit's type, the unit still selected. A filter that
    /// hides it (the search, a nation, a role) is left as it is: the user set it.
    /// </summary>
    public void ShowInUnits(Data.Units.Unit unit)
    {
        // another type tab: the views kept for a sub-tab switch alone were drawn for the type shown before, so they are
        // drawn again (a new helicopter would otherwise be selected while the Tanks tab still shows)
        if (filter.Factory != unit.Factory) ShownThingsChanged();
        filter.Factory = unit.Factory;
        ShowPart(ArmoryPart.Units);
        selection.Selected = unit;
    }

    /// <summary>A weapon, by its descriptor, shown selected on the Weapons sub-tab, as Show in Armory from the Rules tab asks.</summary>
    private void ShowWeapon(string descriptor)
    {
        WeaponsView.SelectWhenShown(descriptor);
        ShowPart(ArmoryPart.Weapons);
    }

    /// <inheritdoc/>
    public bool CanShowWeaponInArmory(string ammunition) => loaded?.Catalog.Weapons?.Any(weapon => weapon.Descriptor == ammunition) == true;

    /// <inheritdoc/>
    public void ShowWeaponInArmory(string ammunition)
    {
        if (!CanShowWeaponInArmory(ammunition)) return;
        ShowPart(ArmoryPart.Weapons);
        WeaponsView.Reveal(ammunition);
    }

    /// <summary>Whether the Unit pane offers Show in Units: on Divisions and Battlegroups.</summary>
    public bool OffersShowInUnits => part != ArmoryPart.Units;
}
