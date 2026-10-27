using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>The categories of the Weapons sub-tab's type tabs, in the order they are listed.</summary>
public enum WeaponCategory
{
    /// <summary>Tank and anti-tank guns (WARNO's "Canon").</summary>
    Gun,
    /// <summary>Autocannons and gatlings.</summary>
    Autocannon,
    /// <summary>Machine guns, medium and heavy.</summary>
    MachineGun,
    /// <summary>Rifles, sub-machine guns, sniper rifles, shotguns, grenades and flamethrowers.</summary>
    SmallArms,
    /// <summary>Anti-tank guided missiles.</summary>
    AntiTankMissile,
    /// <summary>Infantry and artillery rockets.</summary>
    Rocket,
    /// <summary>Anti-air guns and missiles.</summary>
    AntiAir,
    /// <summary>Howitzers, mortars and ballistic missiles.</summary>
    Artillery,
    /// <summary>What aircraft carry: bombs, air-to-ground missiles, rockets, gun pods.</summary>
    Air,
    /// <summary>Smoke.</summary>
    Smoke,
}

/// <summary>
/// Which category a weapon is in. The game names its ammunition by what it is ("Ammo_Canon_AP_120mm",
/// "Ammo_MMG_M240", "Ammo_RocketAir_Hydra"), so the word after "Ammo_" says it; a weapon named otherwise (Steel
/// Division 2's, or a mod's) goes by its sort (<see cref="WeaponKind"/>).
/// </summary>
public static class WeaponCategories
{
    /// <summary>The words after "Ammo_" and their category, in any case ("HMG" and "hmg" are both in the game).</summary>
    private static readonly Dictionary<string, WeaponCategory> ByWord = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Canon"] = WeaponCategory.Gun,
        ["AutoCanon"] = WeaponCategory.Autocannon, ["Gatling"] = WeaponCategory.Autocannon, ["AutoDefense"] = WeaponCategory.Autocannon,
        ["MMG"] = WeaponCategory.MachineGun, ["HMG"] = WeaponCategory.MachineGun, ["SAW"] = WeaponCategory.MachineGun,
        ["FM"] = WeaponCategory.SmallArms, ["PM"] = WeaponCategory.SmallArms, ["Sniper"] = WeaponCategory.SmallArms, ["SniperHvy"] = WeaponCategory.SmallArms,
        ["G3"] = WeaponCategory.SmallArms, ["FAMAS"] = WeaponCategory.SmallArms, ["FAL"] = WeaponCategory.SmallArms, ["MAS36"] = WeaponCategory.SmallArms,
        ["SHOT"] = WeaponCategory.SmallArms, ["Grenade"] = WeaponCategory.SmallArms, ["flamethrower"] = WeaponCategory.SmallArms,
        ["ATGM"] = WeaponCategory.AntiTankMissile, ["Javelin"] = WeaponCategory.AntiTankMissile, ["M47"] = WeaponCategory.AntiTankMissile,
        ["RocketInf"] = WeaponCategory.Rocket, ["RocketArt"] = WeaponCategory.Rocket, ["MLRS"] = WeaponCategory.Rocket,
        ["DCA"] = WeaponCategory.AntiAir, ["AA"] = WeaponCategory.AntiAir, ["SAM"] = WeaponCategory.AntiAir, ["MANPAD"] = WeaponCategory.AntiAir,
        ["Mistral"] = WeaponCategory.AntiAir, ["Starstreak"] = WeaponCategory.AntiAir,
        ["Howz"] = WeaponCategory.Artillery, ["Mortier"] = WeaponCategory.Artillery, ["Lance"] = WeaponCategory.Artillery,
        ["Bomb"] = WeaponCategory.Air, ["AGM"] = WeaponCategory.Air, ["RocketAir"] = WeaponCategory.Air, ["GatlingAir"] = WeaponCategory.Air,
        ["Pod"] = WeaponCategory.Air, ["Pave"] = WeaponCategory.Air, ["MW1"] = WeaponCategory.Air, ["KMGU"] = WeaponCategory.Air,
        ["SMOKE"] = WeaponCategory.Smoke,
    };

    /// <summary>A weapon's category, from its descriptor's first word after "Ammo_", else from its sort.</summary>
    /// <param name="descriptor">The ammunition's descriptor, such as "Ammo_Canon_AP_120mm".</param>
    /// <param name="kind">Its sort, for a descriptor named otherwise.</param>
    public static WeaponCategory Of(string descriptor, WeaponKind kind)
    {
        if (WordOf(descriptor) is string word && ByWord.TryGetValue(word, out WeaponCategory category)) return category;
        return kind switch
        {
            WeaponKind.Missile => WeaponCategory.AntiTankMissile,
            WeaponKind.Rocket => WeaponCategory.Rocket,
            WeaponKind.SmallArms => WeaponCategory.SmallArms,
            WeaponKind.Smoke => WeaponCategory.Smoke,
            _ => WeaponCategory.Gun,
        };
    }

    /// <summary>Whether a weapon is a heavy machine gun ("Ammo_HMG_…"), which the Armory names apart from the others.</summary>
    public static bool IsHeavyMachineGun(string descriptor) => string.Equals(WordOf(descriptor), "HMG", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first word after "Ammo_": "Canon" in "Ammo_Canon_AP_120mm"; null when it is not named so.</summary>
    private static string? WordOf(string descriptor)
    {
        const string prefix = "Ammo_";
        if (!descriptor.StartsWith(prefix, StringComparison.Ordinal)) return null;
        int end = descriptor.IndexOf('_', prefix.Length);
        return end < 0 ? descriptor[prefix.Length..] : descriptor[prefix.Length..end];
    }
}
