namespace EasyNdf.Data.Units;

/// <summary>The sorts of weapon the weapon tables group by, in the order they are listed.</summary>
public enum WeaponKind
{
    /// <summary>Armour-piercing guns and anti-tank weapons that are not missiles or rockets.</summary>
    AntiArmor,
    /// <summary>Guided missiles: anti-tank, anti-air.</summary>
    Missile,
    /// <summary>Rockets and recoilless weapons.</summary>
    Rocket,
    /// <summary>High-explosive guns, howitzers, autocannons, bombs.</summary>
    HighExplosive,
    /// <summary>Rifles and machine guns.</summary>
    SmallArms,
    /// <summary>Smoke.</summary>
    Smoke,
}

/// <summary>Which sort a weapon is, from its name, its ammunition's file and its damage family.</summary>
public static class WeaponKinds
{
    /// <summary>
    /// A weapon's sort. Its name is looked at first ("SMOKE", "Rocket", "Roquette"), then its ammunition (WARNO's
    /// missiles are in AmmunitionMissiles.ndf) and its damage family ("ap", "missile", "Balle"...).
    /// </summary>
    public static WeaponKind Of(UnitWeapon weapon)
    {
        string name = weapon.Name.ToLowerInvariant();
        string family = weapon.DamageFamily.ToLowerInvariant();
        if (name.Contains("smoke", StringComparison.Ordinal)) return WeaponKind.Smoke;
        if (name.Contains("rocket", StringComparison.Ordinal) || name.Contains("roquette", StringComparison.Ordinal) || family.Contains("roquette", StringComparison.Ordinal))
            return WeaponKind.Rocket;
        if (weapon.AmmunitionFile == "AmmunitionMissiles.ndf" || weapon.Missile is not null || family.Contains("missile", StringComparison.Ordinal)
            || name.StartsWith("sam ", StringComparison.Ordinal) || name.StartsWith("atgm", StringComparison.Ordinal))
            return WeaponKind.Missile;
        if (family == "ap") return WeaponKind.AntiArmor;
        if (family.Contains("balle", StringComparison.Ordinal) || family == "cac") return WeaponKind.SmallArms;
        return WeaponKind.HighExplosive;
    }

    /// <summary>A unit's weapons in their groups, groups in <see cref="WeaponKind"/>'s order, each weapon with its place in the unit's list.</summary>
    public static List<(WeaponKind Kind, List<(UnitWeapon Weapon, int Index)> Weapons)> Grouped(IReadOnlyList<UnitWeapon> weapons) =>
        weapons.Select((weapon, index) => (Weapon: weapon, Index: index))
            .GroupBy(item => Of(item.Weapon))
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.ToList()))
            .ToList();
}
