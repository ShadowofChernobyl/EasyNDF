namespace EasyNdf.Data.Weapons;

/// <summary>
/// A unit's loadout, as WeaponDescriptor.ndf lays it out (a TWeaponManagerModuleDescriptor, such as
/// "WeaponDescriptor_M1A1_Abrams_US"): its turrets, each turning a part of the unit's 3D model, the weapons mounted on
/// each, and the ammo boxes they draw from. It belongs to the unit, unlike the weapons (ammunition) it names, which
/// every unit carrying them shares.
/// </summary>
/// <param name="Descriptor">The loadout's descriptor.</param>
/// <param name="AmmoBoxes">
/// The rounds of each ammo box, by its place (Salves): a mounted weapon's AmmoBoxIndex (Steel Division 2's
/// SalvoStockIndex) picks one. Steel Division 2 lists 29 places, those it does not use as -1.
/// </param>
/// <param name="Turrets">The turrets, in the order the file lists them.</param>
/// <param name="MainSalvoCount">
/// How many entries Steel Division 2's SalvoIsMainSalvo has, one for each ammo box in use (some loadouts one more); null
/// for a loadout without that list, as WARNO's.
/// </param>
public sealed record Loadout(string Descriptor, IReadOnlyList<int> AmmoBoxes, IReadOnlyList<LoadoutTurret> Turrets, int? MainSalvoCount = null)
{
    /// <summary>Every mounted weapon, turret by turret.</summary>
    public IEnumerable<LoadoutMount> Mounts => Turrets.SelectMany(turret => turret.Mounts);

    /// <summary>The highest weapon number the loadout gives (WeaponAlternative_N), so a weapon added takes the next.</summary>
    public int HighestWeaponNumber => Mounts.Select(mount => mount.WeaponNumber).DefaultIfEmpty(0).Max();
}

/// <summary>A turret of a loadout: one moving part of the unit's 3D model and the weapons it carries.</summary>
/// <param name="Index">Its place in the loadout's list, from 0.</param>
/// <param name="Tag">Its name in the 3D model, such as "tourelle1".</param>
/// <param name="Type">How it moves, as the game names it: TTurretTwoAxisDescriptor, TTurretUnitDescriptor...</param>
/// <param name="Mounts">The weapons mounted on it.</param>
public sealed record LoadoutTurret(int Index, string Tag, string Type, IReadOnlyList<LoadoutMount> Mounts);

/// <summary>A weapon mounted on a turret (a TMountedWeaponDescriptor).</summary>
/// <param name="Turret">Its turret's place in the loadout.</param>
/// <param name="Index">Its place on the turret, from 0.</param>
/// <param name="Ammunition">Its weapon, by the ammunition's descriptor, such as "Ammo_HMG_12_7_mm_M2HB".</param>
/// <param name="AmmoBox">The ammo box it draws from (AmmoBoxIndex; Steel Division 2's SalvoStockIndex).</param>
/// <param name="Count">How many of the weapon the mount has (NbWeapons).</param>
/// <param name="EffectTag">The flash and sound it fires with, such as "FireEffect_HMG_12_7_mm_M2HB".</param>
/// <param name="WeaponNumber">
/// Its number in the unit, which the 3D model's firing points use: the N of WeaponAlternative_N; in Steel Division 2,
/// which has none, the N of its EffectTag 'weapon_effet_tagN', one of its own in each loadout.
/// </param>
public sealed record LoadoutMount(int Turret, int Index, string Ammunition, int AmmoBox, int Count, string EffectTag, int WeaponNumber);
