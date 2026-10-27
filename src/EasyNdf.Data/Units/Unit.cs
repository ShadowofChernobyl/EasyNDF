namespace EasyNdf.Data.Units;

/// <summary>A unit's armour on each side, as the game's armour levels (0 for none); null where the game gives none.</summary>
public sealed record UnitArmor(int? Front, int? Sides, int? Rear, int? Top);

/// <summary>
/// The armour family of each side of a unit, as WARNO names them, such as "ResistanceFamily_blindage" (tanks) or
/// "ResistanceFamily_helico"; null where the game gives none (Steel Division 2 has no families).
/// </summary>
public sealed record UnitArmorFamilies(string? Front, string? Sides, string? Rear, string? Top);

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
    /// <summary>Its descriptor, such as "Descriptor_Unit_M1A1_Abrams_US".</summary>
    public required string Descriptor { get; init; }

    /// <summary>
    /// The file it is in: UniteDescriptor.ndf, or BuildingDescriptors.ndf for a unit that is a building, such as an FOB.
    /// </summary>
    public string File { get; init; } = "UniteDescriptor.ndf";

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
    /// What the game counts it as in its strategic tallies (TypeStrategicCount), such as "Armor_Heavy"; empty when it
    /// has none. Tells apart units that share a role, such as light and heavy tanks.
    /// </summary>
    public string StrategicType { get; init; } = "";

    /// <summary>Whether it flies (a helicopter or a plane, whatever its category): it finds its way as an aircraft.</summary>
    public bool Flies { get; init; }

    /// <summary>
    /// Its role's in-game name, when the game gives one: Steel Division 2's roles are text keys of the game's own
    /// (TypeSpecificToken). Null for WARNO, whose roles are codes the app names itself.
    /// </summary>
    public string? RoleName { get; init; }

    /// <summary>
    /// The unit it upgrades from (UpgradeFromUnit), by descriptor, such as the M1A1 Abrams's "Descriptor_Unit_M1A1_Abrams_CMD_US";
    /// null when it upgrades from none. The game's deck builder puts each chain of upgrades on a row of its own.
    /// </summary>
    public string? UpgradesFrom { get; init; }

    /// <summary>Its cost in command points.</summary>
    public double? Cost { get; init; }

    /// <summary>Its strength: how much damage it takes to destroy it.</summary>
    public double? Strength { get; init; }

    /// <summary>Its armour.</summary>
    public UnitArmor Armor { get; init; } = new(null, null, null, null);

    /// <summary>Its armour family on each side, which sets the levels its armour can have (<see cref="ArmorLevels"/>).</summary>
    public UnitArmorFamilies ArmorFamilies { get; init; } = new(null, null, null, null);

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

    /// <summary>Its tags (TTagsModuleDescriptor's TagSet), such as "Infanterie" or "Crew": what a transport looks at.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Whether it is a command unit: the game tags those "Commandant" (they have a commander module too). A deck
    /// needs one to be valid.
    /// </summary>
    public bool IsCommandUnit => Tags.Contains("Commandant");

    /// <summary>
    /// As a transport, the tags of the units it can carry (its TTransporterModuleDescriptor's TransportableTagSet), such
    /// as "Crew"; null for a unit that is not a transport.
    /// </summary>
    public IReadOnlyList<string>? CarriesTags { get; init; }

    /// <summary>As a transport, how many seats it has (NbSeatsAvailable); null for a unit that is not one.</summary>
    public double? Seats { get; init; }

    /// <summary>
    /// How many seats it takes in a transport (its TTransportableModuleDescriptor's NbSeatsOccupied); null for a unit
    /// that cannot ride in one, such as a tank.
    /// </summary>
    public double? SeatsTaken { get; init; }

    /// <summary>
    /// Whether the game gives it a number for deck codes (DeckSerializer.ndf's UnitIds). A unit without one cannot be
    /// in a deck, so it is never offered as a transport.
    /// </summary>
    public bool HasDeckNumber { get; init; }

    /// <summary>Its number for deck codes (DeckSerializer.ndf's UnitIds); null when it has none.</summary>
    public int? DeckNumber { get; init; }

    private IReadOnlyList<UnitWeapon> weapons = [];

    /// <summary>Its weapons, in the order its weapon set lists them.</summary>
    public IReadOnlyList<UnitWeapon> Weapons { get => weapons; init => weapons = value; }

    /// <summary>The same unit carrying other weapons, as a mod's weapon changes make it; this one stays as it is.</summary>
    public Unit WithWeapons(IReadOnlyList<UnitWeapon> carried)
    {
        var copy = (Unit)MemberwiseClone();
        copy.weapons = carried;
        return copy;
    }

    /// <summary>
    /// The same unit with a loadout of its own (another weapon set) and the weapons it carries, as a mod's changes to
    /// its weapons make it; this one stays as it is.
    /// </summary>
    public Unit WithLoadout(string loadout, IReadOnlyList<UnitWeapon> carried)
    {
        var copy = (Unit)MemberwiseClone();
        copy.weapons = carried;
        copy.weaponSet = loadout;
        return copy;
    }

    private string? weaponSet;

    /// <summary>Its weapon set in WeaponDescriptor.ndf, such as "WeaponDescriptor_M1A1_Abrams_US"; null when it has none.</summary>
    public string? WeaponSet { get => weaponSet; init => weaponSet = value; }

    /// <summary>The names of the multiplayer divisions it is in.</summary>
    public IReadOnlyList<string> Divisions { get; init; } = [];

    /// <summary>Its picture's path in the game, such as "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/M1A1_Abrams_US.png".</summary>
    public string? PictureSource { get; init; }

    /// <summary>Its longest weapon range; null when it has no weapon with a range.</summary>
    public double? LongestRange => Weapons.Select(w => w.Range).Where(range => range > 0).DefaultIfEmpty().Max();
}

/// <summary>The units of one game or mod, with what was read from where.</summary>
/// <param name="Game">The game the units are from.</param>
/// <param name="Units">The units, in the order of UniteDescriptor.ndf, then BuildingDescriptors.ndf.</param>
/// <param name="FlagSources">Each nation's flag picture, by the nation's code.</param>
/// <param name="Problems">What could not be read, each with what went wrong.</param>
/// <param name="Divisions">Every division, in the order of Divisions.ndf, multiplayer or not.</param>
/// <param name="BattlegroupCards">
/// The descriptor of every battlegroup card in DeckPacks.ndf, so a mod adds only the cards the game does not have.
/// </param>
/// <param name="Loadouts">The units' loadouts by descriptor (WeaponDescriptor.ndf), turret by turret, for the weapons editor.</param>
/// <param name="WeaponPictures">Every weapon picture the game lists (WeaponTextures.ndf): its file, by texture name.</param>
/// <param name="Traits">The weapon traits the game describes, by token, with their names and pictures; empty for none.</param>
/// <param name="ArmorLevels">How many levels each armour family has, and each kind of weapon damage ("DamageFamily_ap"), from DamageResistance.ndf; empty when it was not found.</param>
public sealed record UnitCatalog(
    Game.GameTitle Game, IReadOnlyList<Unit> Units, IReadOnlyDictionary<string, string> FlagSources, IReadOnlyList<string> Problems,
    IReadOnlyList<Divisions.Division> Divisions, IReadOnlySet<string>? BattlegroupCards = null, IReadOnlyDictionary<string, int>? ArmorLevels = null,
    IReadOnlyList<Weapons.Weapon>? Weapons = null, IReadOnlyDictionary<string, Weapons.Loadout>? Loadouts = null,
    IReadOnlyDictionary<string, Weapons.WeaponTrait>? Traits = null, IReadOnlyDictionary<string, string>? WeaponPictures = null);

/// <summary>
/// The armour levels the game allows: an armour family's levels run from 1 to the number DamageResistance.ndf gives it
/// (ResistanceFamilyCounts). GenerateMod stops with "Index must be between 1 and 4 for this family" on any other.
/// </summary>
public static class ArmorLevels
{
    /// <summary>The lowest and highest level of an armour family; null when the family or its count is not known.</summary>
    public static (int Lowest, int Highest)? RangeOf(IReadOnlyDictionary<string, int>? levels, string? family)
    {
        if (levels is null || family is null || !levels.TryGetValue(family, out int count) || count < 1) return null;
        return (1, count);
    }

    /// <summary>Whether a level is one the family allows; any level when its range is not known.</summary>
    public static bool Allows(IReadOnlyDictionary<string, int>? levels, string? family, int level) =>
        RangeOf(levels, family) is not (int lowest, int highest) || (level >= lowest && level <= highest);
}
