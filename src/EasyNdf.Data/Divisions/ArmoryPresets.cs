using System.Text.Json.Nodes;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The mod's Armory presets, one for each part of the Armory with changes (Divisions, Battlegroups, Units, Weapons): its changes
/// as rules (<see cref="DivisionChangeRules"/>), made again whenever they change, followed by the rules made editable
/// from them, which are kept as they are. A part with neither has no preset. They are listed under Armory Changes on
/// the Rules tab, apart from the mod's other presets.
/// </summary>
public static class ArmoryPresets
{
    /// <summary>The Armory's parts in the order their presets are listed and run.</summary>
    public static IReadOnlyList<ArmoryPart> Parts { get; } = [ArmoryPart.Divisions, ArmoryPart.Battlegroups, ArmoryPart.Units, ArmoryPart.Weapons];

    /// <summary>The mod's preset for a part of the Armory, or null when it has none.</summary>
    public static ModPreset? Of(Mod mod, ArmoryPart part) => mod.Presets.FirstOrDefault(preset => preset.ArmoryPart == part);

    /// <summary>The mod's Armory presets, in the order they are listed and run.</summary>
    public static IReadOnlyList<ModPreset> AllOf(Mod mod) => [.. Parts.Select(part => Of(mod, part)).OfType<ModPreset>()];

    /// <summary>The rules of all the mod's Armory presets, in the order they run; the presets' own, not copies.</summary>
    public static IReadOnlyList<JsonNode?> RulesOf(Mod mod) => [.. AllOf(mod).SelectMany(preset => preset.Rules)];

    /// <summary>Whether the mod has an Armory preset.</summary>
    public static bool HasAny(Mod mod) => mod.Presets.Any(preset => preset.IsArmoryPreset);

    /// <summary>
    /// Whether a locked rule is in another part's preset, as in mods saved before the Armory had parts, when one
    /// preset kept them all: the presets are then made again, each rule going to its own part's. Division headings
    /// are in every part's preset that has rules for the division.
    /// </summary>
    public static bool HasRulesOutOfPlace(Mod mod) =>
        AllOf(mod).Any(preset => preset.Rules.Any(rule => IsLocked(rule) && (string?)rule?["Kind"] != "Group" && DivisionChangeRules.PartOf(rule) != preset.ArmoryPart));

    /// <summary>Whether a rule is one the Armory made and keeps up to date: it is changed there, not on the Rules tab.</summary>
    public static bool IsLocked(JsonNode? rule) =>
        rule?[DivisionChangeRules.ArmoryKey] is JsonObject armory && !JsonValues.IsFalse(armory[DivisionChangeRules.LockedKey]);

    /// <summary>
    /// Makes the mod's Armory presets say what its Armory changes are now: a part's preset is added when it has none,
    /// and taken away when nothing is left in it.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's divisions, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="unitName">A unit's name as the Armory shows it, by its descriptor.</param>
    /// <param name="presetName">The name a part's new preset gets ("Divisions").</param>
    /// <param name="gameCards">The battlegroup cards the game has (<see cref="Units.UnitCatalog.BattlegroupCards"/>).</param>
    /// <param name="gameUnits">The game's units, which the units the mod adds are made from; none, for a mod that adds none.</param>
    /// <param name="gameWeapons">The game's weapons, which the mod's weapon changes are made on; none, for a mod that changes none.</param>
    /// <param name="gameLoadouts">The game's loadouts, which the mod's changes to units' weapons are made on.</param>
    public static void Update(Mod mod, IReadOnlyList<Division> game, TextCatalog texts, Func<string, string> unitName,
        Func<ArmoryPart, string> presetName, IReadOnlySet<string>? gameCards = null, IReadOnlyList<Units.Unit>? gameUnits = null,
        IReadOnlyList<Weapons.Weapon>? gameWeapons = null, IReadOnlyDictionary<string, Weapons.Loadout>? gameLoadouts = null)
    {
        Dictionary<ArmoryPart, JsonArray> made = SplitByPart(RulesFor(mod, game, texts, unitName, gameCards, gameUnits ?? [], gameWeapons ?? [], gameLoadouts));
        foreach (ArmoryPart part in Parts) UpdatePart(mod, part, made.GetValueOrDefault(part) ?? [], presetName(part));
    }

    /// <summary>The rules made from the Armory's changes: the divisions', then the units', then the weapons'.</summary>
    public static JsonArray RulesFor(Mod mod, IReadOnlyList<Division> game, TextCatalog texts, Func<string, string> unitName,
        IReadOnlySet<string>? gameCards, IReadOnlyList<Units.Unit> gameUnits, IReadOnlyList<Weapons.Weapon> gameWeapons,
        IReadOnlyDictionary<string, Weapons.Loadout>? gameLoadouts = null)
    {
        JsonArray rules = DivisionChangeRules.For(mod, game, texts, unitName, gameCards);
        foreach (JsonNode? rule in Units.UnitChangeRules.For(mod, gameUnits, texts, gameLoadouts)) rules.Add(rule!.DeepClone());
        foreach (JsonNode? rule in Weapons.WeaponChangeRules.For(mod, gameWeapons, texts, loadout => UnitsWithLoadout(gameUnits, loadout))) rules.Add(rule!.DeepClone());
        return rules;
    }

    /// <summary>The names of the game's units with a loadout (WeaponDescriptor), for the name of a rule that changes it.</summary>
    private static string UnitsWithLoadout(IReadOnlyList<Units.Unit> gameUnits, string loadout)
    {
        List<string> names = [.. gameUnits.Where(unit => unit.WeaponSet == loadout).Select(unit => unit.Name)];
        return names.Count == 0 ? loadout : string.Join(", ", names);
    }

    /// <summary>One part's preset: the rules made for it, then the rules kept by hand in it.</summary>
    private static void UpdatePart(Mod mod, ArmoryPart part, JsonArray rules, string presetName)
    {
        ModPreset? preset = Of(mod, part);
        foreach (JsonNode? kept in preset?.Rules.Where(rule => !IsLocked(rule)) ?? []) rules.Add(kept!.DeepClone());
        if (rules.Count == 0)
        {
            if (preset is not null) mod.Presets.Remove(preset);
            return;
        }
        if (preset is null)
        {
            preset = new ModPreset { Name = mod.FreePresetName(presetName), ArmoryPart = part };
            mod.Presets.Add(preset);
        }
        preset.Rules = rules;
    }

    /// <summary>
    /// The rules made from the Armory's changes, each in its part's list (see <see cref="DivisionChangeRules.PartOf"/>),
    /// under a copy of its division's heading: a part gets a division's heading only when it has rules for it.
    /// </summary>
    private static Dictionary<ArmoryPart, JsonArray> SplitByPart(JsonArray rules)
    {
        var split = new Dictionary<ArmoryPart, JsonArray>();
        var headingGiven = new Dictionary<ArmoryPart, JsonNode>();
        JsonNode? heading = null;
        foreach (JsonNode? rule in rules)
        {
            if ((string?)rule?["Kind"] == "Group")
            {
                heading = rule;
                continue;
            }
            ArmoryPart part = DivisionChangeRules.PartOf(rule);
            if (!split.TryGetValue(part, out JsonArray? list)) split[part] = list = [];
            if (heading is not null && !ReferenceEquals(headingGiven.GetValueOrDefault(part), heading))
            {
                list.Add(heading.DeepClone());
                headingGiven[part] = heading;
            }
            list.Add(rule!.DeepClone());
        }
        return split;
    }

    /// <summary>
    /// Deleting rules of an Armory preset on the Rules tab takes back the Armory changes they make, so the Armory and
    /// the rules never disagree: a game division gets those values back as the game has them, and a division or unit
    /// the mod makes is deleted whole, as its rules only make sense together (a unit is taken out of the divisions too); its battlegroup's rules take back only its
    /// battlegroup, so deleting in the Battlegroups preset never empties the Divisions preset. Rules made editable are
    /// only deleted.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's divisions, before the mod's changes.</param>
    /// <param name="rules">The rules deleted, from an Armory preset.</param>
    public static void TakeBack(Mod mod, IReadOnlyList<Division> game, IEnumerable<JsonNode?> rules)
    {
        foreach (JsonNode? rule in rules)
        {
            if (!IsLocked(rule) || rule![DivisionChangeRules.ArmoryKey] is not JsonObject armory) continue;
            if ((string?)armory[Units.UnitChangeRules.UnitKey] is string unit)
            {
                TakeBackUnit(mod, unit, (string?)armory[DivisionChangeRules.ChangeKey]);
                continue;
            }
            if ((string?)armory[Weapons.WeaponChangeRules.WeaponKey] is string weapon)
            {
                TakeBackWeapon(mod, weapon, (string?)armory[DivisionChangeRules.ChangeKey]);
                continue;
            }
            string division = (string?)armory[DivisionChangeRules.DivisionKey] ?? "";
            if (DivisionChanges.EntryOf(mod, division) is not ModDivision entry) continue;
            if (entry.IsNew && DivisionChangeRules.PartOf(rule) == ArmoryPart.Battlegroups)
            {
                LetGoOfBattlegroup(entry);
                continue;
            }
            if (entry.IsNew)
            {
                mod.Divisions.Remove(entry);
                continue;
            }
            if (game.FirstOrDefault(each => each.Descriptor == entry.StartsFrom) is not Division start) continue;
            entry.LetGoOf((string?)armory[DivisionChangeRules.ChangeKey] ?? "", start);
            if (entry.ChangesNothing) mod.Divisions.Remove(entry);
        }
    }

    /// <summary>
    /// A unit's rules deleted: a change to one of the game's units is taken back whole, the unit as the game has it
    /// again; a unit the mod adds is deleted, and taken out of the mod's divisions.
    /// </summary>
    private static void TakeBackUnit(Mod mod, string unit, string? change)
    {
        if (change == Units.UnitChangeRules.EditChange) mod.UnitEdits.RemoveAll(edit => edit.Descriptor == unit);
        else Units.ModUnit.RemoveFrom(mod, unit);
    }

    /// <summary>
    /// A weapon's rules deleted: a change to one of the game's weapons is taken back whole, the weapon as the game has
    /// it again; a weapon the mod adds is deleted, with the copies made from it, and the loadouts given it carry their
    /// original again.
    /// </summary>
    private static void TakeBackWeapon(Mod mod, string weapon, string? change)
    {
        if (change == Weapons.WeaponChangeRules.EditChange) mod.WeaponEdits.RemoveAll(edit => edit.Descriptor == weapon);
        else Weapons.ModWeapon.RemoveFrom(mod, weapon);
    }

    /// <summary>
    /// Make Editable: the rules of one change are kept by hand from now on, and the Armory lets go of the change, so
    /// the two cannot both make it. For a new division, every one of its rules is kept, and the Armory lets go of the
    /// whole division; from the Battlegroups preset, only its battlegroup's rules are kept, and the Armory lets go of
    /// its battlegroup only.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's divisions, before the mod's changes.</param>
    /// <param name="rule">One of the change's rules, from an Armory preset.</param>
    public static void MakeEditable(Mod mod, IReadOnlyList<Division> game, JsonNode rule)
    {
        if (rule[DivisionChangeRules.ArmoryKey] is not JsonObject armory) return;
        if ((string?)armory[Units.UnitChangeRules.UnitKey] is string unit)
        {
            MakeUnitEditable(mod, unit);
            return;
        }
        if ((string?)armory[Weapons.WeaponChangeRules.WeaponKey] is string weapon)
        {
            MakeWeaponEditable(mod, weapon);
            return;
        }
        string division = (string?)armory[DivisionChangeRules.DivisionKey] ?? "";
        string change = (string?)armory[DivisionChangeRules.ChangeKey] ?? "";
        ArmoryPart part = DivisionChangeRules.PartOf(rule);
        ModDivision? entry = DivisionChanges.EntryOf(mod, division);
        // a new division let go of whole takes its battlegroup's rules with it: its deck names the division
        bool wholeNewDivision = entry is { IsNew: true } && part != ArmoryPart.Battlegroups;
        foreach (JsonNode? each in RulesOf(mod))
        {
            if (each?[DivisionChangeRules.ArmoryKey] is not JsonObject other) continue;
            if (!wholeNewDivision && DivisionChangeRules.PartOf(each) != part) continue;
            if ((string?)other[DivisionChangeRules.DivisionKey] == division && (string?)other[DivisionChangeRules.ChangeKey] == change)
                other[DivisionChangeRules.LockedKey] = false;
        }
        if (entry is null) return;
        if (entry.IsNew && part == ArmoryPart.Battlegroups)
        {
            LetGoOfBattlegroup(entry);
            return;
        }
        if (entry.IsNew)
        {
            mod.Divisions.Remove(entry);
            return;
        }
        if (game.FirstOrDefault(each => each.Descriptor == entry.StartsFrom) is not Division start) return;
        entry.LetGoOf(change, start);
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>
    /// A unit the mod adds, or its change to one of the game's, made editable: every one of its rules is kept by hand
    /// from now on, and the Armory lets go of the unit. The rules still make it, so the divisions that use it keep it.
    /// </summary>
    private static void MakeUnitEditable(Mod mod, string unit)
    {
        foreach (JsonNode? each in RulesOf(mod))
            if (each?[DivisionChangeRules.ArmoryKey] is JsonObject other && (string?)other[Units.UnitChangeRules.UnitKey] == unit)
                other[DivisionChangeRules.LockedKey] = false;
        mod.Units.RemoveAll(each => each.Descriptor == unit);
        mod.UnitEdits.RemoveAll(each => each.Descriptor == unit);
    }

    /// <summary>
    /// A weapon the mod adds, or its change to one of the game's, made editable: every one of its rules is kept by hand
    /// from now on, and the Armory lets go of the weapon. The rules still make it, so the loadouts given it keep it.
    /// </summary>
    private static void MakeWeaponEditable(Mod mod, string weapon)
    {
        foreach (JsonNode? each in RulesOf(mod))
            if (each?[DivisionChangeRules.ArmoryKey] is JsonObject other && (string?)other[Weapons.WeaponChangeRules.WeaponKey] == weapon)
                other[DivisionChangeRules.LockedKey] = false;
        mod.Weapons.RemoveAll(each => each.Descriptor == weapon);
        mod.WeaponEdits.RemoveAll(each => each.Descriptor == weapon);
    }

    /// <summary>A new division's battlegroup let go of: the division keeps everything else, its battlegroup empty again, as it started.</summary>
    private static void LetGoOfBattlegroup(ModDivision entry)
    {
        entry.BattlegroupCards = null;
        entry.BattlegroupName = "";
    }
}
