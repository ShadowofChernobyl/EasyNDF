using System.Windows;
using System.Windows.Controls;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// What the parts that show units (the Unit pane, Compare's cards, the weapon tables) can do with them; the tab that
/// holds the parts does it. Kept apart so another tab can show units with the same parts.
/// </summary>
public interface IUnitActions
{
    /// <summary>The game the units are from, for its side names and its unit of range.</summary>
    GameTitle Game { get; }

    /// <summary>Opens a unit's menu, at the mouse over <paramref name="at"/>, or under it when it is a ▾ button.</summary>
    ContextMenu OpenUnitMenu(Unit unit, FrameworkElement at);

    /// <summary>Opens a weapon's menu, at the mouse over <paramref name="at"/>, or under it when it is a ▾ button.</summary>
    ContextMenu OpenWeaponMenu(Unit unit, UnitWeapon weapon, FrameworkElement at);

    /// <summary>Whether a weapon, by its ammunition, is on the Armory's Weapons sub-tab, to be shown there.</summary>
    bool CanShowWeaponInArmory(string ammunition) => false;

    /// <summary>"Show in Weapons": the Weapons sub-tab, with the weapon selected and in view.</summary>
    void ShowWeaponInArmory(string ammunition) { }

    /// <summary>Whether the tab knows the other units, so the Unit pane can say which share the unit's loadout and weapons.</summary>
    bool KnowsOtherUnits => false;

    /// <summary>The other units with the same loadout (WeaponDescriptor) as a unit, which carry the same weapons.</summary>
    IReadOnlyList<Unit> UnitsSharingLoadout(Unit unit) => [];

    /// <summary>How many units carry a weapon, by its ammunition; 0 when not known.</summary>
    int CarriersOf(string ammunition) => 0;

    /// <summary>Whether a unit's file is on the Files tab, so it can be shown in the File Editor.</summary>
    bool CanShowInEditor(Unit unit);

    /// <summary>Shows a unit in the File Editor.</summary>
    void ShowInEditor(Unit unit);

    /// <summary>Makes a new rule for a unit, on the Rules tab.</summary>
    void NewRuleFor(Unit unit);

    /// <summary>Adds a unit to Compare, and shows Compare.</summary>
    void Compare(Unit unit);

    /// <summary>Whether a division named in the Unit pane can be opened with a click: on the Armory tab, it can.</summary>
    bool CanShowDivisions => false;

    /// <summary>Opens a division named in the Unit pane, by its name, where the tab shows divisions.</summary>
    void ShowDivisionCalled(string name) { }

    /// <summary>Whether the Unit pane offers Show in Units: where the unit is shown apart from the list of every unit.</summary>
    bool OffersShowInUnits => false;

    /// <summary>Shows the unit among every unit, on the Armory's Units sub-tab.</summary>
    void ShowInUnits(Unit unit) { }

    /// <summary>Whether a unit can be duplicated into the open mod: on the Armory tab, the source's own and the mod's.</summary>
    bool CanDuplicate(Unit unit) => false;

    /// <summary>Duplicates a unit into the open mod, under a name asked for.</summary>
    void Duplicate(Unit unit) { }

    /// <summary>
    /// Whether a unit's values can be changed in the Unit pane: on the Armory tab, for WARNO, the source's own units
    /// and those the open mod adds.
    /// </summary>
    bool CanChangeUnit(Unit unit) => false;

    /// <summary>
    /// Changes one of a unit's values in the open mod, by its field (<see cref="UnitField"/>): its name, its nation or
    /// role (by code), or a stat as typed.
    /// </summary>
    /// <returns>Whether it was changed: false for a value the unit does not take, or a change refused.</returns>
    bool ChangeUnit(Unit unit, string field, string value) => false;

    /// <summary>The nations a unit can be given, by code, each named with its side: "United States (NATO)".</summary>
    IReadOnlyList<(string Code, string Shown)> NationChoices => [];

    /// <summary>The roles a unit can be given, by code, each with its name.</summary>
    IReadOnlyList<(string Code, string Shown)> RoleChoices => [];

    /// <summary>The levels of each armour family, from the game's DamageResistance.ndf; null when not known.</summary>
    IReadOnlyDictionary<string, int>? ArmorLevels => null;

    /// <summary>Whether a unit's card picture can be changed from the Unit pane: on the Armory tab, any unit that can be changed.</summary>
    bool CanChangePicture(Unit unit) => false;

    /// <summary>Opens the menu of the Unit pane's ✎ on a unit's picture, under <paramref name="at"/>.</summary>
    void OpenPictureMenu(Unit unit, FrameworkElement at) { }

    /// <summary>
    /// The weapons editor for a unit, its loadout turret by turret with the open mod's changes; null where a unit's
    /// weapons cannot be changed (another tab, Steel Division 2, a loadout the editor cannot lay out).
    /// </summary>
    FrameworkElement? LoadoutEditorFor(Unit unit) => null;

    /// <summary>Whether a unit is on the game's first side (NATO, the Allies), for its colour.</summary>
    bool IsFirstSide(Unit unit) => unit.Side == UnitNames.SidesOf(Game)[0];
}
