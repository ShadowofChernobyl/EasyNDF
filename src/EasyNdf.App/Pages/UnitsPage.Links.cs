using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// What the Unit pane's "Linked To" box asks the tab: the units sharing a unit's loadout, and how many units carry each
/// weapon, among the units shown (as the open mod makes them).
/// </summary>
public partial class UnitsPage
{
    /// <inheritdoc/>
    public bool KnowsOtherUnits => loaded is not null;

    /// <inheritdoc/>
    public IReadOnlyList<Unit> UnitsSharingLoadout(Unit unit)
    {
        if (loaded is null || unit.WeaponSet is not string loadout) return [];
        return [.. loaded.Catalog.Units.Where(other => other.WeaponSet == loadout && other.Descriptor != unit.Descriptor)];
    }

    /// <inheritdoc/>
    public int CarriersOf(string ammunition) =>
        loaded?.Catalog.Weapons?.FirstOrDefault(weapon => weapon.Descriptor == ammunition) is Weapon weapon ? weapon.Users.Count : 0;
}
