using EasyNdf.App.Controls;
using EasyNdf.App.Models;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.App.Pages;

/// <summary>
/// "Show 3D Model…" on a unit's menu, a missile's menu and the New Unit window: the 3D model viewer, with the unit's
/// turrets as the open mod has them.
/// </summary>
public partial class UnitsPage
{
    /// <summary>
    /// "Show 3D Model…" for a unit; dim, with the reason on hover, for Steel Division 2 (whose models cannot be read yet)
    /// and for a unit whose files name no model.
    /// </summary>
    private MenuChoice ShowModelChoice(Unit unit)
    {
        string? why = WhyNoModel(unit);
        return new MenuChoice(Texts.Get("Units.Model.Menu.Show"), () => ShowModel(unit), IsEnabled: why is null, Tip: why ?? Texts.Get("Units.Model.Menu.Show.Tip"));
    }

    /// <summary>Why a unit's model cannot be shown; null when it can.</summary>
    private string? WhyNoModel(Unit unit)
    {
        if (!ModelViewer.CanShow(Game)) return Texts.Get("Units.Model.Menu.OtherGame.Tip");
        return unit.Look is null ? Texts.Get("Units.Model.Menu.NoModel.Tip") : null;
    }

    /// <summary>
    /// Opens the viewer on a unit: its turrets come from its loadout as the Armory shows it (the open mod's), so their
    /// limits and speeds are the mod's; the unit as the source's files have it goes with it, for the viewer's Mod
    /// Changes switch.
    /// </summary>
    public void ShowModel(Unit unit)
    {
        if (loaded is null || WhyNoModel(unit) is not null) return;
        Loadout? loadout = unit.WeaponSet is string set
            ? loaded.Catalog.Loadouts?.GetValueOrDefault(set) ?? gameUnits?.Catalog.Loadouts?.GetValueOrDefault(set)
            : null;
        Unit? original = gameUnits?.Catalog.Units.FirstOrDefault(each => each.Descriptor == unit.Descriptor);
        Loadout? originalLoadout = original?.WeaponSet is string originalSet ? gameUnits?.Catalog.Loadouts?.GetValueOrDefault(originalSet) : null;
        var originalWeapons = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in gameUnits?.Catalog.Weapons ?? []) originalWeapons.TryAdd(weapon.Descriptor, weapon);
        MainView.Of(this).ModelViewer.OpenUnit(unit, original, loaded.Source, loadout, originalLoadout, WeaponsShown(), originalWeapons);
    }

    /// <inheritdoc/>
    public string? WhyNoModel(Weapon weapon)
    {
        if (!ModelViewer.CanShow(Game)) return Texts.Get("Units.Model.Menu.OtherGame.Tip");
        return null;
    }

    /// <inheritdoc/>
    public void ShowModel(Weapon weapon)
    {
        if (loaded is null || WhyNoModel(weapon) is not null) return;
        MainView.Of(this).ModelViewer.OpenWeapon(weapon.Name, weapon.Descriptor, weapon.Missile, loaded.Source);
    }

    /// <summary>The weapons as the Armory shows them (the open mod's values), by their ammunition's descriptor.</summary>
    private Dictionary<string, Weapon> WeaponsShown()
    {
        var shown = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in loaded?.Catalog.Weapons ?? []) shown.TryAdd(weapon.Descriptor, weapon);
        return shown;
    }
}
