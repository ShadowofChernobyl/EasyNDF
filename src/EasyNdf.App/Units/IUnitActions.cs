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

    /// <summary>Whether a unit's file is on the Files tab, so it can be shown in the File Editor.</summary>
    bool CanShowInEditor(Unit unit);

    /// <summary>Shows a unit in the File Editor.</summary>
    void ShowInEditor(Unit unit);

    /// <summary>Makes a new rule for a unit, on the Rules tab.</summary>
    void NewRuleFor(Unit unit);

    /// <summary>Adds a unit to Compare, and shows Compare.</summary>
    void Compare(Unit unit);

    /// <summary>Whether a unit is on the game's first side (NATO, the Allies), for its colour.</summary>
    bool IsFirstSide(Unit unit) => unit.Side == UnitNames.SidesOf(Game)[0];
}
