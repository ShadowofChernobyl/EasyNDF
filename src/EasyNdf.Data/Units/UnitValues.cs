using System.Text.Json.Nodes;

namespace EasyNdf.Data.Units;

/// <summary>
/// The values the Armory lets the user give a unit: its nation, role, card picture and stats. Each is null to keep the
/// value of the unit it is given to, so only what the user changes is written. Shared by the units a mod adds
/// (<see cref="ModUnit"/>) and its changes to the game's own units (<see cref="UnitEdit"/>).
/// </summary>
public abstract class UnitValues
{
    /// <summary>Its nation's code, such as "US"; null to keep the unit's.</summary>
    public string? Nation { get; set; }

    /// <summary>Its side, "NATO" or "PACT"; null to keep the unit's.</summary>
    public string? Side { get; set; }

    /// <summary>Its role, such as "armor"; null to keep the unit's.</summary>
    public string? Role { get; set; }

    /// <summary>A card picture of the user's own, kept with the app's data; null for the unit's own picture.</summary>
    public string? PictureFile { get; set; }

    /// <summary>Its cost in command points; null to keep the unit's.</summary>
    public int? Cost { get; set; }

    /// <summary>Its strength; null to keep the unit's.</summary>
    public int? Strength { get; set; }

    /// <summary>Its armour levels on each side; null to keep the unit's.</summary>
    public int? FrontArmor { get; set; }

    /// <inheritdoc cref="FrontArmor"/>
    public int? SideArmor { get; set; }

    /// <inheritdoc cref="FrontArmor"/>
    public int? RearArmor { get; set; }

    /// <inheritdoc cref="FrontArmor"/>
    public int? TopArmor { get; set; }

    /// <summary>Its speed off road, in km/h; null to keep the unit's.</summary>
    public int? Speed { get; set; }

    /// <summary>Its speed on roads, in km/h; null to keep the unit's.</summary>
    public int? RoadSpeed { get; set; }

    /// <summary>Its fuel; null to keep the unit's.</summary>
    public int? Fuel { get; set; }

    /// <summary>Its optics; null to keep the unit's.</summary>
    public double? Optics { get; set; }

    /// <summary>How far it sees; null to keep the unit's.</summary>
    public double? Vision { get; set; }

    /// <summary>Its stealth; null to keep the unit's.</summary>
    public double? Stealth { get; set; }

    /// <summary>
    /// The changes to its weapons (swapped, taken off, added), by their places in the loadout of the unit it is given to;
    /// none to keep its weapons. With any, the unit gets a loadout of its own (<see cref="Weapons.LoadoutRules"/>).
    /// </summary>
    public List<Weapons.LoadoutChange> LoadoutChanges { get; set; } = [];

    /// <summary>
    /// The loadout the weapon changes are made on, by descriptor (the unit's, or for a unit the mod adds, its model's),
    /// kept with them: with the mod's own export as the Armory's source, the unit already has its own loadout there,
    /// and the changes' places are in this one.
    /// </summary>
    public string? LoadoutFrom { get; set; }

    /// <summary>A stat given, by its field (<see cref="UnitField"/>); null when it is not given, or is not a stat.</summary>
    public double? Given(string field) => field switch
    {
        UnitField.Cost => Cost,
        UnitField.Strength => Strength,
        UnitField.FrontArmor => FrontArmor,
        UnitField.SideArmor => SideArmor,
        UnitField.RearArmor => RearArmor,
        UnitField.TopArmor => TopArmor,
        UnitField.Speed => Speed,
        UnitField.RoadSpeed => RoadSpeed,
        UnitField.Fuel => Fuel,
        UnitField.Optics => Optics,
        UnitField.Vision => Vision,
        UnitField.Stealth => Stealth,
        _ => null,
    };

    /// <summary>
    /// Gives a stat, by its field (<see cref="UnitField"/>), or takes it back with null. The values the game writes as
    /// whole numbers are rounded to one.
    /// </summary>
    public void Give(string field, double? value)
    {
        int? whole = value is double number ? (int)Math.Round(number) : null;
        switch (field)
        {
            case UnitField.Cost: Cost = whole; break;
            case UnitField.Strength: Strength = whole; break;
            case UnitField.FrontArmor: FrontArmor = whole; break;
            case UnitField.SideArmor: SideArmor = whole; break;
            case UnitField.RearArmor: RearArmor = whole; break;
            case UnitField.TopArmor: TopArmor = whole; break;
            case UnitField.Speed: Speed = whole; break;
            case UnitField.RoadSpeed: RoadSpeed = whole; break;
            case UnitField.Fuel: Fuel = whole; break;
            case UnitField.Optics: Optics = value; break;
            case UnitField.Vision: Vision = value; break;
            case UnitField.Stealth: Stealth = value; break;
        }
    }

    /// <summary>The fields of the values given (<see cref="UnitField"/>), for the marks that show them as changed.</summary>
    public IEnumerable<string> FieldsGiven()
    {
        if (LoadoutChanges.Count > 0) yield return UnitField.Weapons;
        if (Nation is not null) yield return UnitField.Nation;
        if (Role is not null) yield return UnitField.Role;
        foreach (EditableStat stat in EditableStat.All)
            if (Given(stat.Field) is not null) yield return stat.Field;
    }

    /// <summary>Whether it gives any value at all.</summary>
    public bool GivesAnything => Nation is not null || Side is not null || Role is not null || PictureFile is not null || LoadoutChanges.Count > 0
        || EditableStat.All.Any(stat => Given(stat.Field) is not null);

    /// <summary>
    /// A unit as it is with these values: <paramref name="unit"/> with the values given in place of its own, and
    /// everything else (weapons, specialties, model) its own.
    /// </summary>
    /// <param name="unit">The unit the values are given to.</param>
    /// <param name="descriptor">The descriptor of the unit made.</param>
    /// <param name="name">Its name.</param>
    /// <param name="file">The file it is in.</param>
    /// <param name="deckNumber">Its number for deck codes, or null for none.</param>
    /// <param name="divisions">The divisions it is in.</param>
    protected Unit Over(Unit unit, string descriptor, string name, string file, int? deckNumber, IReadOnlyList<string> divisions) => new()
    {
        Descriptor = descriptor,
        File = file,
        Name = name,
        Side = Side ?? unit.Side,
        Nation = Nation ?? unit.Nation,
        Factory = unit.Factory,
        Role = Role ?? unit.Role,
        // the role's name in the game's own words goes with the game's role only
        RoleName = Role is null ? unit.RoleName : null,
        UpgradesFrom = unit.UpgradesFrom,
        Cost = Cost ?? unit.Cost,
        Strength = Strength ?? unit.Strength,
        ArmorFamilies = unit.ArmorFamilies,
        Armor = new UnitArmor(FrontArmor ?? unit.Armor.Front, SideArmor ?? unit.Armor.Sides, RearArmor ?? unit.Armor.Rear, TopArmor ?? unit.Armor.Top),
        Speed = Speed ?? unit.Speed,
        RoadSpeed = RoadSpeed ?? unit.RoadSpeed,
        Optics = Optics ?? unit.Optics,
        Vision = Vision ?? unit.Vision,
        Stealth = Stealth ?? unit.Stealth,
        Fuel = Fuel ?? unit.Fuel,
        Specialties = unit.Specialties,
        Tags = unit.Tags,
        CarriesTags = unit.CarriesTags,
        Seats = unit.Seats,
        SeatsTaken = unit.SeatsTaken,
        HasDeckNumber = deckNumber is not null,
        DeckNumber = deckNumber,
        Weapons = unit.Weapons,
        WeaponSet = unit.WeaponSet,
        Divisions = divisions,
        PictureSource = PictureFile ?? unit.PictureSource,
    };

    /// <summary>
    /// The armour levels given that the game does not allow for the sides' armour families (see <see cref="ArmorLevels"/>),
    /// each as its side's name and level; none when every one is allowed or the families are not known.
    /// </summary>
    /// <param name="unit">The unit the values are given to, whose armour families they are.</param>
    /// <param name="levels">The levels of each armour family, from the game's DamageResistance.ndf.</param>
    public List<(string Side, int Level, string Family)> ArmorOutOfRange(Unit unit, IReadOnlyDictionary<string, int>? levels)
    {
        var wrong = new List<(string, int, string)>();
        void Check(string side, int? level, string? family)
        {
            if (level is int given && family is not null && !ArmorLevels.Allows(levels, family, given)) wrong.Add((side, given, family));
        }
        Check("Front", FrontArmor, unit.ArmorFamilies.Front);
        Check("Sides", SideArmor, unit.ArmorFamilies.Sides);
        Check("Rear", RearArmor, unit.ArmorFamilies.Rear);
        Check("Top", TopArmor, unit.ArmorFamilies.Top);
        return wrong;
    }

    // ---- the mod file ----

    /// <summary>Adds the values given to an entry of the mod file; those not given are left out.</summary>
    protected void WriteValues(JsonObject json)
    {
        AddIfGiven(json, "Nation", Nation);
        AddIfGiven(json, "Side", Side);
        AddIfGiven(json, "Role", Role);
        AddIfGiven(json, "PictureFile", PictureFile);
        AddIfGiven(json, "Cost", Cost);
        AddIfGiven(json, "Strength", Strength);
        AddIfGiven(json, "FrontArmor", FrontArmor);
        AddIfGiven(json, "SideArmor", SideArmor);
        AddIfGiven(json, "RearArmor", RearArmor);
        AddIfGiven(json, "TopArmor", TopArmor);
        AddIfGiven(json, "Speed", Speed);
        AddIfGiven(json, "RoadSpeed", RoadSpeed);
        AddIfGiven(json, "Fuel", Fuel);
        AddIfGiven(json, "Optics", Optics);
        AddIfGiven(json, "Vision", Vision);
        AddIfGiven(json, "Stealth", Stealth);
        if (LoadoutChanges.Count > 0) json["Loadout"] = new JsonArray([.. LoadoutChanges.Select(change => (JsonNode)change.ToJson())]);
        AddIfGiven(json, "LoadoutFrom", LoadoutFrom);
    }

    /// <summary>Reads the values of an entry of the mod file; what it does not have stays null, kept from the unit.</summary>
    protected void ReadValues(JsonObject json)
    {
        Nation = Text(json["Nation"]);
        Side = Text(json["Side"]);
        Role = Text(json["Role"]);
        PictureFile = Text(json["PictureFile"]);
        Cost = WholeNumber(json["Cost"]);
        Strength = WholeNumber(json["Strength"]);
        FrontArmor = WholeNumber(json["FrontArmor"]);
        SideArmor = WholeNumber(json["SideArmor"]);
        RearArmor = WholeNumber(json["RearArmor"]);
        TopArmor = WholeNumber(json["TopArmor"]);
        Speed = WholeNumber(json["Speed"]);
        RoadSpeed = WholeNumber(json["RoadSpeed"]);
        Fuel = WholeNumber(json["Fuel"]);
        Optics = Number(json["Optics"]);
        Vision = Number(json["Vision"]);
        Stealth = Number(json["Stealth"]);
        LoadoutChanges = [.. (json["Loadout"] as JsonArray ?? []).Select(Weapons.LoadoutChange.FromJson).OfType<Weapons.LoadoutChange>()];
        LoadoutFrom = Text(json["LoadoutFrom"]);
    }

    /// <summary>
    /// A unit as it is with these changes to its weapons: with a loadout of its own (named for <paramref name="owner"/>)
    /// and the weapons the changes leave it. The same unit when there are none, or its loadout is not known.
    /// </summary>
    /// <param name="unit">The unit, its other values given already.</param>
    /// <param name="owner">The unit whose own loadout it is, by descriptor.</param>
    /// <param name="loadouts">The loadouts by descriptor, the unit's among them.</param>
    /// <param name="weapons">The weapons by descriptor, to show those it carries.</param>
    public Unit WithLoadoutChanges(Unit unit, string owner, IReadOnlyDictionary<string, Weapons.Loadout>? loadouts, IReadOnlyDictionary<string, Weapons.Weapon> weapons)
    {
        if (LoadoutChanges.Count == 0 || (LoadoutFrom ?? unit.WeaponSet) is not string set || loadouts?.GetValueOrDefault(set) is not Weapons.Loadout loadout) return unit;
        Weapons.Loadout changed = Weapons.LoadoutRules.Changed(loadout, LoadoutChanges, Weapons.LoadoutRules.OwnLoadoutName(owner));
        return unit.WithLoadout(changed.Descriptor, Weapons.LoadoutRules.WeaponsOf(changed, weapons));
    }

    /// <summary>Adds a text to an entry of the mod file, when it is given.</summary>
    protected static void AddIfGiven(JsonObject json, string key, string? value)
    {
        if (value is not null) json[key] = value;
    }

    /// <summary>Adds a number to an entry of the mod file, when it is given.</summary>
    protected static void AddIfGiven(JsonObject json, string key, double? value)
    {
        if (value is double number) json[key] = number;
    }

    /// <summary>Adds a whole number to an entry of the mod file, when it is given.</summary>
    protected static void AddIfGiven(JsonObject json, string key, int? value)
    {
        if (value is int number) json[key] = number;
    }

    /// <summary>A text of the mod file; null where it has none.</summary>
    protected static string? Text(JsonNode? node) => node is null ? null : JsonValues.AsText(node);

    /// <summary>A whole number of the mod file; null where it has none.</summary>
    protected static int? WholeNumber(JsonNode? node) => Number(node) is double number ? (int)Math.Round(number) : null;

    /// <summary>
    /// A number, read from a file or made in code: a value made from a whole number in code is not read as a double,
    /// so it is tried as one first.
    /// </summary>
    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out int whole)) return whole;
        return value.TryGetValue(out double number) ? number : null;
    }
}
