namespace EasyNdf.Data.Units;

/// <summary>A unit's armour on each side, as the game's armour levels (0 for none); null where the game gives none.</summary>
public sealed record UnitArmor(int? Front, int? Sides, int? Rear, int? Top);

/// <summary>One of a unit's weapons, with the numbers from its ammunition.</summary>
/// <param name="Name">The weapon's name, made from its ammunition's: "Ammo_Canon_AP_120_mm_L52_M68" is "Canon AP 120 mm L52 M68".</param>
/// <param name="Count">How many of it the unit has (its NbWeapons), 1 at least.</param>
/// <param name="DamageFamily">Its kind of damage, as the game names it: "ap", "he", "fmballe" (WARNO), "AP", "HE", "Balle" (Steel Division 2).</param>
/// <param name="DamageLevel">The level of that damage, such as 32 for AP 32; 0 when the game gives none.</param>
/// <param name="Range">Its longest range, against ground, helicopters or planes, in the game's range unit.</param>
/// <param name="Damage">Its physical damage.</param>
/// <param name="Suppression">Its suppression damage.</param>
/// <param name="SalvoSeconds">The seconds between two salvos.</param>
/// <param name="Ammunition">The ammunition's descriptor, such as "Ammo_Canon_AP_120_mm_L52_M68".</param>
/// <param name="AmmunitionFile">The file it is in: "Ammunition.ndf", or "AmmunitionMissiles.ndf" for WARNO's missiles.</param>
/// <param name="Missile">The missile's descriptor in MissileDescriptors.ndf, for a missile; otherwise null.</param>
public sealed record UnitWeapon(
    string Name, int Count, string DamageFamily, int DamageLevel, double? Range, double? Damage, double? Suppression,
    double? SalvoSeconds, string Ammunition, string? AmmunitionFile, string? Missile);

/// <summary>A unit, with what the Units tab shows of it.</summary>
public sealed class Unit
{
    /// <summary>Its descriptor in UniteDescriptor.ndf, such as "Descriptor_Unit_M1A1_Abrams_US".</summary>
    public required string Descriptor { get; init; }

    /// <summary>Its in-game name; when the game has none, a name made from its descriptor.</summary>
    public required string Name { get; init; }

    /// <summary>Its side: "NATO" or "PACT" (WARNO), "Allied" or "Axis" (Steel Division 2).</summary>
    public string Side { get; init; } = "";

    /// <summary>Its nation's code, such as "US" or "SOV".</summary>
    public string Nation { get; init; } = "";

    /// <summary>The factory it is bought from, such as "Tanks" or "Infantry".</summary>
    public string Factory { get; init; } = "";

    /// <summary>Its role, such as "armor" (WARNO) or "TArmor" (Steel Division 2); empty when it has none.</summary>
    public string Role { get; init; } = "";

    /// <summary>
    /// Its role's in-game name, when the game gives one: Steel Division 2's roles are text keys of the game's own
    /// (TypeSpecificToken). Null for WARNO, whose roles are codes the app names itself.
    /// </summary>
    public string? RoleName { get; init; }

    /// <summary>Its cost in command points.</summary>
    public double? Cost { get; init; }

    /// <summary>Its strength: how much damage it takes to destroy it.</summary>
    public double? Strength { get; init; }

    /// <summary>Its armour.</summary>
    public UnitArmor Armor { get; init; } = new(null, null, null, null);

    /// <summary>Its speed off road, in km/h.</summary>
    public double? Speed { get; init; }

    /// <summary>Its speed on roads, in km/h.</summary>
    public double? RoadSpeed { get; init; }

    /// <summary>Its optics: how well it spots.</summary>
    public double? Optics { get; init; }

    /// <summary>How far it sees, in the game's range unit.</summary>
    public double? Vision { get; init; }

    /// <summary>Its stealth: how hard it is to spot.</summary>
    public double? Stealth { get; init; }

    /// <summary>Its fuel.</summary>
    public double? Fuel { get; init; }

    /// <summary>Its specialties, as the game names them.</summary>
    public IReadOnlyList<string> Specialties { get; init; } = [];

    /// <summary>Its weapons, in the order its weapon set lists them.</summary>
    public IReadOnlyList<UnitWeapon> Weapons { get; init; } = [];

    /// <summary>Its weapon set in WeaponDescriptor.ndf, such as "WeaponDescriptor_M1A1_Abrams_US"; null when it has none.</summary>
    public string? WeaponSet { get; init; }

    /// <summary>The names of the multiplayer divisions it is in.</summary>
    public IReadOnlyList<string> Divisions { get; init; } = [];

    /// <summary>Its picture's path in the game, such as "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/M1A1_Abrams_US.png".</summary>
    public string? PictureSource { get; init; }

    /// <summary>Its longest weapon range; null when it has no weapon with a range.</summary>
    public double? LongestRange => Weapons.Select(w => w.Range).Where(range => range > 0).DefaultIfEmpty().Max();
}

/// <summary>The units of one game or mod, with what was read from where.</summary>
/// <param name="Game">The game the units are from.</param>
/// <param name="Units">The units, in the order of UniteDescriptor.ndf.</param>
/// <param name="FlagSources">Each nation's flag picture, by the nation's code.</param>
/// <param name="Problems">What could not be read, each with what went wrong.</param>
/// <param name="Divisions">Every division, in the order of Divisions.ndf, multiplayer or not.</param>
public sealed record UnitCatalog(
    Game.GameTitle Game, IReadOnlyList<Unit> Units, IReadOnlyDictionary<string, string> FlagSources, IReadOnlyList<string> Problems,
    IReadOnlyList<Divisions.Division> Divisions);
