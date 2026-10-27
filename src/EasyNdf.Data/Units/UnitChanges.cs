using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Units;

/// <summary>
/// The names of a unit's values that the Armory shows, by which a changed value is found: the stats by their keys in
/// <see cref="UnitStat"/>, a weapon's values by its place in the unit's list.
/// </summary>
public static class UnitField
{
    /// <summary>The name.</summary>
    public const string Name = "Name";

    /// <summary>The nation (and with it the side), and the role.</summary>
    public const string Nation = "Nation", Role = "Role";

    /// <summary>The cost in command points.</summary>
    public const string Cost = "Cost";

    /// <summary>The strength (its health).</summary>
    public const string Strength = "Strength";

    /// <summary>The armour on each side.</summary>
    public const string FrontArmor = "FrontArmor", SideArmor = "SideArmor", RearArmor = "RearArmor", TopArmor = "TopArmor";

    /// <summary>The speed, and on roads.</summary>
    public const string Speed = "Speed", RoadSpeed = "RoadSpeed";

    /// <summary>The optics, vision, stealth and fuel.</summary>
    public const string Optics = "Optics", Vision = "Vision", Stealth = "Stealth", Fuel = "Fuel";

    /// <summary>The longest range of its weapons.</summary>
    public const string LongestRange = "LongestRange";

    /// <summary>How many weapons it has.</summary>
    public const string Weapons = "Weapons";

    /// <summary>The specialties, and the divisions it is in.</summary>
    public const string Specialties = "Specialties", Divisions = "Divisions";

    /// <summary>The values of a weapon, after its place in the list.</summary>
    public const string WeaponName = "Name", WeaponCount = "Count", WeaponRange = "Range", WeaponDamage = "Damage",
        WeaponSuppression = "Suppression", WeaponSalvo = "Salvo";

    /// <summary>A weapon's value, by the weapon's place in the unit's list: "Weapon 2 Range".</summary>
    public static string OfWeapon(int index, string value) => "Weapon " + index + " " + value;
}

/// <summary>
/// How a unit's values differ between the game's files and the files as a mod's rules make them: which of its values
/// changed, the unit as the game has it, and which of the mod's rules made each change.
/// </summary>
public sealed class UnitChange
{
    private readonly Dictionary<string, IReadOnlyList<RuleOfMod>> rulesByField;

    internal UnitChange(Unit? game, Dictionary<string, IReadOnlyList<RuleOfMod>> rulesByField)
    {
        Game = game;
        this.rulesByField = rulesByField;
    }

    /// <summary>The unit as the game has it; null for a unit the mod adds.</summary>
    public Unit? Game { get; }

    /// <summary>Whether the mod adds the unit (a clone, for one): every value it has is the mod's.</summary>
    public bool IsNew => Game is null;

    /// <summary>The values that changed.</summary>
    public IEnumerable<string> Fields => rulesByField.Keys;

    /// <summary>Whether a value changed (any value, for a unit the mod adds).</summary>
    public bool Changed(string field) => IsNew || rulesByField.ContainsKey(field);

    /// <summary>The rules that changed a value, each with its mod, as best the edit log shows it; empty when not known.</summary>
    public IReadOnlyList<RuleOfMod> RulesFor(string field) => rulesByField.GetValueOrDefault(field) ?? [];
}

/// <summary>A rule that changed a value, and the mod it is in: with mods running together, another mod's rule can change a value the Armory shows.</summary>
/// <param name="Mod">The mod's name.</param>
/// <param name="Rule">The rule's name.</param>
public sealed record RuleOfMod(string Mod, string Rule);

/// <summary>One edit a rule made, as its log gives it: the rule's name, where it changed, the property, and the rule's mod.</summary>
/// <param name="Rule">The rule's name.</param>
/// <param name="Descriptor">The descriptor changed, as the log names it: "Descriptor_Unit_X", or "Descriptor_Unit_X > TModule" inside it.</param>
/// <param name="Target">The property the rule changed.</param>
/// <param name="Mod">The rule's mod; empty when it is the mod the changes are compared for.</param>
public sealed record RuleEdit(string Rule, string Descriptor, string Target, string Mod = "")
{
    /// <summary>The edits of a run, from its log, each with its rule's name and the mod given.</summary>
    public static IEnumerable<RuleEdit> From(RunResult result, string mod = "")
    {
        for (int rule = 0; rule < result.Stats.Count && rule < result.Logs.Count; rule++)
            foreach (EditLogEntry entry in result.Logs[rule])
                yield return new RuleEdit(result.Stats[rule].Name, entry.Descriptor, entry.Target, mod);
    }

    /// <summary>The rule, with its mod: the mod given when the edit names none.</summary>
    public RuleOfMod Of(string modName) => new(Mod == "" ? modName : Mod, Rule);
}

/// <summary>
/// The units a mod's rules change, against the game's: for each unit whose values differ (or that the mod adds),
/// which values, and which rules.
/// </summary>
public sealed class UnitChanges
{
    private readonly Dictionary<string, UnitChange> byDescriptor;

    private UnitChanges(string modName, Dictionary<string, UnitChange> byDescriptor)
    {
        ModName = modName;
        this.byDescriptor = byDescriptor;
    }

    /// <summary>No changes: the open mod changes nothing the Armory shows.</summary>
    public static UnitChanges None { get; } = new("", []);

    /// <summary>The mod whose rules made the changes.</summary>
    public string ModName { get; }

    /// <summary>How many units changed or were added.</summary>
    public int Count => byDescriptor.Count;

    /// <summary>How a unit changed; null when it did not.</summary>
    public UnitChange? Of(Unit unit) => byDescriptor.GetValueOrDefault(unit.Descriptor);

    /// <summary>
    /// The same changes with the units the mod adds in the Armory (<see cref="ModUnit"/>), which no rule run made:
    /// every value of each is the mod's.
    /// </summary>
    /// <param name="modName">The mod's name, for the tooltips.</param>
    /// <param name="descriptors">The units added, by descriptor.</param>
    public UnitChanges WithAdded(string modName, IEnumerable<string> descriptors)
    {
        var all = new Dictionary<string, UnitChange>(byDescriptor, StringComparer.Ordinal);
        foreach (string descriptor in descriptors) all[descriptor] = new UnitChange(null, []);
        return new UnitChanges(modName, all);
    }

    /// <summary>
    /// The same changes with the open mod's changes to the game's units made in the Armory (<see cref="UnitEdit"/>),
    /// which no rule run made: a value the change gives is named as changed by its rule; a value the mod's other rules
    /// change keeps their names.
    /// </summary>
    /// <param name="modName">The mod's name, for the tooltips.</param>
    /// <param name="edited">Each unit changed: as the game has it, as it is shown, the fields the change gives and its rule's name.</param>
    public UnitChanges WithEdited(string modName, IEnumerable<(Unit Game, Unit Shown, IReadOnlyCollection<string> Fields, string Rule)> edited)
    {
        var all = new Dictionary<string, UnitChange>(byDescriptor, StringComparer.Ordinal);
        foreach ((Unit game, Unit shown, IReadOnlyCollection<string> fieldsGiven, string rule) in edited)
        {
            UnitChange? byOtherRules = all.GetValueOrDefault(shown.Descriptor);
            var rules = new Dictionary<string, IReadOnlyList<RuleOfMod>>(StringComparer.Ordinal);
            foreach (string field in FieldsChanged(game, shown))
                rules[field] = fieldsGiven.Contains(field) ? [new RuleOfMod(modName, rule)] : byOtherRules?.RulesFor(field) ?? [];
            if (rules.Count == 0) all.Remove(shown.Descriptor);
            else all[shown.Descriptor] = new UnitChange(game, rules);
        }
        return new UnitChanges(modName, all);
    }

    /// <summary>
    /// Compares the units as the mod makes them with the game's.
    /// </summary>
    /// <param name="modName">The mod's name, for the tooltips.</param>
    /// <param name="game">The units as the game has them.</param>
    /// <param name="changed">The same units read from the files as the mod's rules make them.</param>
    /// <param name="edits">The edits the rules made, to tell which rule changed what.</param>
    public static UnitChanges Between(string modName, UnitCatalog game, UnitCatalog changed, IReadOnlyList<RuleEdit> edits)
    {
        Dictionary<string, List<RuleEdit>> editsByDescriptor = ByDescriptor(edits);
        var gameUnits = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in game.Units) gameUnits.TryAdd(unit.Descriptor, unit);
        var found = new Dictionary<string, UnitChange>(StringComparer.Ordinal);
        foreach (Unit unit in changed.Units)
        {
            if (!gameUnits.TryGetValue(unit.Descriptor, out Unit? before))
            {
                found[unit.Descriptor] = new UnitChange(null, []);
                continue;
            }
            List<string> fields = FieldsChanged(before, unit);
            if (fields.Count == 0) continue;
            var rules = new Dictionary<string, IReadOnlyList<RuleOfMod>>(StringComparer.Ordinal);
            foreach (string field in fields) rules[field] = RulesThatChanged(modName, unit, field, edits, editsByDescriptor);
            found[unit.Descriptor] = new UnitChange(before, rules);
        }
        return new UnitChanges(modName, found);
    }

    /// <summary>The values that differ between the game's unit and the mod's.</summary>
    public static List<string> FieldsChanged(Unit game, Unit changed)
    {
        var fields = new List<string>();
        void Compare(string field, object? before, object? after)
        {
            if (!Equals(before, after)) fields.Add(field);
        }
        Compare(UnitField.Name, game.Name, changed.Name);
        Compare(UnitField.Nation, game.Nation, changed.Nation);
        Compare(UnitField.Role, game.Role, changed.Role);
        Compare(UnitField.Cost, game.Cost, changed.Cost);
        Compare(UnitField.Strength, game.Strength, changed.Strength);
        Compare(UnitField.FrontArmor, game.Armor.Front, changed.Armor.Front);
        Compare(UnitField.SideArmor, game.Armor.Sides, changed.Armor.Sides);
        Compare(UnitField.RearArmor, game.Armor.Rear, changed.Armor.Rear);
        Compare(UnitField.TopArmor, game.Armor.Top, changed.Armor.Top);
        Compare(UnitField.Speed, game.Speed, changed.Speed);
        Compare(UnitField.RoadSpeed, game.RoadSpeed, changed.RoadSpeed);
        Compare(UnitField.Optics, game.Optics, changed.Optics);
        Compare(UnitField.Vision, game.Vision, changed.Vision);
        Compare(UnitField.Stealth, game.Stealth, changed.Stealth);
        Compare(UnitField.Fuel, game.Fuel, changed.Fuel);
        Compare(UnitField.LongestRange, game.LongestRange, changed.LongestRange);
        Compare(UnitField.Weapons, game.Weapons.Count, changed.Weapons.Count);
        // lists are compared as their items joined, in order
        Compare(UnitField.Specialties, string.Join("\n", game.Specialties), string.Join("\n", changed.Specialties));
        Compare(UnitField.Divisions, string.Join("\n", game.Divisions), string.Join("\n", changed.Divisions));
        for (int index = 0; index < changed.Weapons.Count; index++)
        {
            UnitWeapon after = changed.Weapons[index];
            UnitWeapon? before = index < game.Weapons.Count ? game.Weapons[index] : null;
            Compare(UnitField.OfWeapon(index, UnitField.WeaponName), before?.Name, after.Name);
            Compare(UnitField.OfWeapon(index, UnitField.WeaponCount), before?.Count, after.Count);
            Compare(UnitField.OfWeapon(index, UnitField.WeaponRange), before?.Range, after.Range);
            Compare(UnitField.OfWeapon(index, UnitField.WeaponDamage), before?.Damage, after.Damage);
            Compare(UnitField.OfWeapon(index, UnitField.WeaponSuppression), before?.Suppression, after.Suppression);
            Compare(UnitField.OfWeapon(index, UnitField.WeaponSalvo), before?.SalvoSeconds, after.SalvoSeconds);
        }
        return fields;
    }

    /// <summary>
    /// The rules that changed a value: those whose edits are in the descriptors the value comes from (the unit's own,
    /// or for a weapon its ammunition and the unit's weapon set). Where some of those edits are to the properties the
    /// value is read from, only their rules are named.
    /// </summary>
    private static IReadOnlyList<RuleOfMod> RulesThatChanged(string modName, Unit unit, string field, IReadOnlyList<RuleEdit> edits, Dictionary<string, List<RuleEdit>> editsByDescriptor)
    {
        List<RuleEdit> inThem;
        if (field == UnitField.Divisions)
        {
            // a unit's divisions change in the divisions' own descriptors
            inThem = edits.Where(edit => edit.Descriptor.StartsWith("Descriptor_Deck_", StringComparison.Ordinal)).ToList();
        }
        else
        {
            inThem = [.. DescriptorsOf(unit, field).Distinct(StringComparer.Ordinal).SelectMany(descriptor => editsByDescriptor.GetValueOrDefault(descriptor) ?? [])];
        }
        string[] properties = PropertiesOf(field);
        List<RuleEdit> toProperties = inThem.Where(edit => properties.Contains(LastPart(edit.Target), StringComparer.Ordinal)).ToList();
        return (toProperties.Count > 0 ? toProperties : inThem).Select(edit => edit.Of(modName)).Distinct().ToList();
    }

    /// <summary>
    /// The edits by the descriptor they are in ("Descriptor_Unit_X > TModule" is in Descriptor_Unit_X), so each
    /// unit's are found at once among the thousands a mod can make.
    /// </summary>
    private static Dictionary<string, List<RuleEdit>> ByDescriptor(IReadOnlyList<RuleEdit> edits)
    {
        var byDescriptor = new Dictionary<string, List<RuleEdit>>(StringComparer.Ordinal);
        foreach (RuleEdit edit in edits)
        {
            int inside = edit.Descriptor.IndexOf(" > ", StringComparison.Ordinal);
            string descriptor = inside < 0 ? edit.Descriptor : edit.Descriptor[..inside];
            if (!byDescriptor.TryGetValue(descriptor, out List<RuleEdit>? list)) byDescriptor[descriptor] = list = [];
            list.Add(edit);
        }
        return byDescriptor;
    }

    /// <summary>The descriptors a value comes from.</summary>
    private static List<string> DescriptorsOf(Unit unit, string field)
    {
        var descriptors = new List<string> { unit.Descriptor };
        bool isAboutWeapons = field.StartsWith("Weapon", StringComparison.Ordinal) || field == UnitField.LongestRange;
        if (!isAboutWeapons) return descriptors;
        if (unit.WeaponSet is not null) descriptors.Add(unit.WeaponSet);
        foreach (UnitWeapon weapon in unit.Weapons)
        {
            descriptors.Add(weapon.Ammunition);
            if (weapon.Missile is not null) descriptors.Add(weapon.Missile);
        }
        return descriptors;
    }

    /// <summary>
    /// The property of the game's files a value is in, as the Armory's panes show it under the value's name: WARNO's
    /// name, which comes first; null for a value with none.
    /// </summary>
    public static string? FileNameOf(string field) => PropertiesOf(field).FirstOrDefault();

    /// <summary>The properties of the game's files a value is read from (see <see cref="UnitReader"/>).</summary>
    private static string[] PropertiesOf(string field) => field switch
    {
        UnitField.Name => ["NameToken"],
        UnitField.Nation => ["MotherCountry", "Coalition"],
        UnitField.Role => ["UnitRole"],
        UnitField.Cost => ["ProductionRessourcesNeeded", "Resource_CommandPoints"],
        UnitField.Strength => ["MaxPhysicalDamages", "MaxDamages"],
        UnitField.FrontArmor => ["ResistanceFront", "ArmorDescriptorFront", "Index"],
        UnitField.SideArmor => ["ResistanceSides", "ArmorDescriptorSides", "Index"],
        UnitField.RearArmor => ["ResistanceRear", "ArmorDescriptorRear", "Index"],
        UnitField.TopArmor => ["ResistanceTop", "ArmorDescriptorTop", "Index"],
        UnitField.Speed => ["MaxSpeedInKmph", "Maxspeed"],
        UnitField.RoadSpeed => ["DisplayRoadSpeedInKmph", "RealRoadSpeed"],
        UnitField.Optics => ["OpticalStrengths", "OpticalStrength"],
        UnitField.Vision => ["VisionRangesGRU", "PorteeVision"],
        UnitField.Stealth => ["UnitConcealmentBonus"],
        UnitField.Fuel => ["FuelCapacity"],
        UnitField.Specialties => ["SpecialtiesList"],
        _ => [],
    };

    /// <summary>The last part of a target path ("A > B" gives "B"), or the target itself.</summary>
    private static string LastPart(string target)
    {
        int at = target.LastIndexOf(ActionApplier.PathSeparator, StringComparison.Ordinal);
        return at < 0 ? target : target[(at + ActionApplier.PathSeparator.Length)..];
    }
}
