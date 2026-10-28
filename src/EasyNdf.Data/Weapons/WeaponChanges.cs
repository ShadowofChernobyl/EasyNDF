using EasyNdf.Data.Units;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// How a weapon's values differ between the game's files and the weapon as a mod makes it: which of its values changed,
/// the weapon as the game has it, and which of the mod's rules made each change. The weapons' twin of <see cref="UnitChange"/>.
/// </summary>
public sealed class WeaponChange
{
    private readonly Dictionary<string, IReadOnlyList<RuleOfMod>> rulesByField;

    internal WeaponChange(Weapon? game, Dictionary<string, IReadOnlyList<RuleOfMod>> rulesByField)
    {
        Game = game;
        this.rulesByField = rulesByField;
    }

    /// <summary>The weapon as the game has it; null for a weapon the mod adds.</summary>
    public Weapon? Game { get; }

    /// <summary>Whether the mod adds the weapon (a copy): every value it has is the mod's.</summary>
    public bool IsNew => Game is null;

    /// <summary>The values that changed.</summary>
    public IEnumerable<string> Fields => rulesByField.Keys;

    /// <summary>Whether a value changed (any value, for a weapon the mod adds).</summary>
    public bool Changed(string field) => IsNew || rulesByField.ContainsKey(field);

    /// <summary>The rules that changed a value, each with its mod, as best the edit log shows it; empty when not known.</summary>
    public IReadOnlyList<RuleOfMod> RulesFor(string field) => rulesByField.GetValueOrDefault(field) ?? [];
}

/// <summary>
/// The weapons a mod changes, against the game's: for each weapon whose values differ (or that the mod adds), which
/// values, and which rules. Its rules' changes are found by comparing the weapons read from the files they make; its
/// Armory changes (<see cref="WeaponEdit"/>, <see cref="ModWeapon"/>) are added with <see cref="WithArmory"/>.
/// </summary>
public sealed class WeaponChanges
{
    private readonly Dictionary<string, WeaponChange> byDescriptor;

    private WeaponChanges(string modName, Dictionary<string, WeaponChange> byDescriptor)
    {
        ModName = modName;
        this.byDescriptor = byDescriptor;
    }

    /// <summary>No changes: the open mod changes no weapon.</summary>
    public static WeaponChanges None { get; } = new("", []);

    /// <summary>The mod whose rules made the changes.</summary>
    public string ModName { get; }

    /// <summary>How many weapons changed or were added.</summary>
    public int Count => byDescriptor.Count;

    /// <summary>The weapons changed or added, by descriptor.</summary>
    public IEnumerable<string> Descriptors => byDescriptor.Keys;

    /// <summary>How a weapon changed; null when it did not.</summary>
    public WeaponChange? Of(Weapon weapon) => byDescriptor.GetValueOrDefault(weapon.Descriptor);

    /// <summary>
    /// Compares the weapons as the mod's rules make them with the game's.
    /// </summary>
    /// <param name="modName">The mod's name, for the tooltips.</param>
    /// <param name="game">The weapons as the game has them.</param>
    /// <param name="changed">The same weapons read from the files as the mod's rules make them.</param>
    /// <param name="edits">The edits the rules made, to tell which rule changed what.</param>
    public static WeaponChanges Between(string modName, IReadOnlyList<Weapon> game, IReadOnlyList<Weapon> changed, IReadOnlyList<RuleEdit> edits)
    {
        var gameWeapons = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in game) gameWeapons.TryAdd(weapon.Descriptor, weapon);
        var found = new Dictionary<string, WeaponChange>(StringComparer.Ordinal);
        foreach (Weapon weapon in changed)
        {
            if (!gameWeapons.TryGetValue(weapon.Descriptor, out Weapon? before))
            {
                found[weapon.Descriptor] = new WeaponChange(null, []);
                continue;
            }
            List<string> fields = FieldsChanged(before, weapon);
            if (fields.Count == 0) continue;
            List<RuleEdit> inIt = [.. edits.Where(edit => DescriptorOf(edit) == weapon.Descriptor)];
            var rules = new Dictionary<string, IReadOnlyList<RuleOfMod>>(StringComparer.Ordinal);
            foreach (string field in fields) rules[field] = RulesThatChanged(modName, field, inIt);
            found[weapon.Descriptor] = new WeaponChange(before, rules);
        }
        return new WeaponChanges(modName, found);
    }

    /// <summary>
    /// The same changes with the open mod's Armory changes, which no rule run made: each weapon changed (with the
    /// game's weapon) or added (without), as it is shown, and the rule that gives each value it changes. A value the
    /// mod's other rules change keeps their names.
    /// </summary>
    /// <param name="modName">The mod's name, for the tooltips.</param>
    /// <param name="changed">Each weapon changed or added in the Armory.</param>
    public WeaponChanges WithArmory(string modName, IEnumerable<(Weapon? Game, Weapon Shown, IReadOnlyDictionary<string, string> RuleByField)> changed)
    {
        var all = new Dictionary<string, WeaponChange>(byDescriptor, StringComparer.Ordinal);
        foreach ((Weapon? game, Weapon shown, IReadOnlyDictionary<string, string> ruleByField) in changed)
        {
            if (game is null)
            {
                all[shown.Descriptor] = new WeaponChange(null, []);
                continue;
            }
            WeaponChange? byOtherRules = all.GetValueOrDefault(shown.Descriptor);
            var rules = new Dictionary<string, IReadOnlyList<RuleOfMod>>(StringComparer.Ordinal);
            foreach (string field in FieldsChanged(game, shown))
                rules[field] = ruleByField.TryGetValue(field, out string? rule) ? [new RuleOfMod(modName, rule)] : byOtherRules?.RulesFor(field) ?? [];
            if (rules.Count == 0) all.Remove(shown.Descriptor);
            else all[shown.Descriptor] = new WeaponChange(game, rules);
        }
        return new WeaponChanges(modName, all);
    }

    /// <summary>The values that differ between the game's weapon and the mod's, and the units carrying it.</summary>
    public static List<string> FieldsChanged(Weapon game, Weapon changed)
    {
        var fields = new List<string>();
        if (game.Name != changed.Name) fields.Add(WeaponField.Name);
        foreach (WeaponStat stat in WeaponStat.All)
            if (!Equals(stat.Read(game), stat.Read(changed))) fields.Add(stat.Field);
        if (game.PictureTexture != changed.PictureTexture) fields.Add(WeaponField.Picture);
        if (!game.Traits.SequenceEqual(changed.Traits, StringComparer.Ordinal)) fields.Add(WeaponField.Traits);
        // the values kept as the file writes them, switches, choices and those only shown: a rule may change any of them
        foreach (string name in WeaponFileValues.Names)
            if (!fields.Contains(name) && game.FileValue(name) != changed.FileValue(name)) fields.Add(name);
        if (!game.Users.SequenceEqual(changed.Users, StringComparer.Ordinal)) fields.Add(WeaponField.Users);
        return fields;
    }

    /// <summary>
    /// The rules that changed a value: of the edits in the weapon's ammunition, those to the property the value is read
    /// from; all of them when none is.
    /// </summary>
    private static IReadOnlyList<RuleOfMod> RulesThatChanged(string modName, string field, List<RuleEdit> inIt)
    {
        string? property = PropertyOf(field);
        List<RuleEdit> toProperty = [.. inIt.Where(edit => property is not null && edit.Target.Contains(property, StringComparison.Ordinal))];
        return [.. (toProperty.Count > 0 ? toProperty : inIt).Select(edit => edit.Of(modName)).Distinct()];
    }

    /// <summary>The ammunition's property a value is in: "Arme" for the damage level; null for one in no single property.</summary>
    private static string? PropertyOf(string field)
    {
        if (field == WeaponField.Picture) return "InterfaceWeaponTexture";
        if (field == WeaponField.Traits) return "TraitsToken";
        string? target = WeaponStat.Of(field)?.Target ?? (WeaponFileValues.Names.Contains(field) ? field : null);
        return target?.Split(ActionApplier.PathSeparator)[0];
    }

    /// <summary>The descriptor an edit is in: "Ammo_X > TDiceHitRollRuleDescriptor" is in Ammo_X.</summary>
    private static string DescriptorOf(RuleEdit edit)
    {
        int inside = edit.Descriptor.IndexOf(" > ", StringComparison.Ordinal);
        return inside < 0 ? edit.Descriptor : edit.Descriptor[..inside];
    }
}
