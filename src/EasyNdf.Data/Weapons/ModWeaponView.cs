using EasyNdf.Data.Languages;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The weapons and units as the open mod's Armory weapon changes make them, for the Armory to show: its changes to the
/// game's weapons (<see cref="WeaponEdit"/>) and the copies it adds (<see cref="ModWeapon"/>), with the units that
/// carry them carrying them as changed, and every weapon's users worked out again. Kept the same as what the rules
/// write (<see cref="WeaponChangeRules"/>), which the tests check by running them.
/// </summary>
public static class ModWeaponView
{
    /// <summary>What the mod's weapon changes make of the units and weapons shown.</summary>
    /// <param name="Catalog">The units and weapons, as changed.</param>
    /// <param name="WeaponMarks">Each weapon changed or added: as the game has it (null for a copy), as shown, and the rule giving each value it changes.</param>
    /// <param name="UnitMarks">Each of the game's units whose weapons changed: as the game has it, as shown, the values changed, and the rules.</param>
    public sealed record Result(
        UnitCatalog Catalog,
        IReadOnlyList<(Weapon? Game, Weapon Shown, IReadOnlyDictionary<string, string> RuleByField)> WeaponMarks,
        IReadOnlyList<(Unit Game, Unit Shown, IReadOnlyCollection<string> Fields, string Rule)> UnitMarks);

    /// <summary>
    /// The units and weapons shown, with the mod's weapon changes. The same catalog is given back when the mod changes
    /// no weapon.
    /// </summary>
    /// <param name="mod">The open mod.</param>
    /// <param name="shown">The units and weapons as the mod's rules (and its other Armory changes) make them.</param>
    /// <param name="game">The units and weapons as the source has them, before any rule, for the marks.</param>
    /// <param name="texts">The language the rules are named in.</param>
    public static Result Apply(Mod mod, UnitCatalog shown, UnitCatalog game, TextCatalog texts)
    {
        if ((mod.WeaponEdits.Count == 0 && mod.Weapons.Count == 0) || shown.Weapons is null) return new Result(shown, [], []);
        Dictionary<string, Weapon> gameWeapons = ByDescriptor(game.Weapons ?? []);
        var weapons = new List<Weapon>(shown.Weapons);
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int at = 0; at < weapons.Count; at++) places.TryAdd(weapons[at].Descriptor, at);
        var ruleNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (WeaponEdit edit in mod.WeaponEdits)
        {
            if (edit.ChangesNothing || !places.TryGetValue(edit.Descriptor, out int at) || !gameWeapons.TryGetValue(edit.Descriptor, out Weapon? gameWeapon)) continue;
            weapons[at] = edit.Apply(weapons[at]);
            ruleNames[edit.Descriptor] = WeaponChangeRules.EditRuleName(gameWeapon, texts);
        }
        foreach (ModWeapon copy in mod.Weapons)
        {
            if (!places.TryGetValue(copy.MadeFrom, out int from)) continue;
            Weapon made = copy.Apply(weapons[from]);
            ruleNames[copy.Descriptor] = WeaponChangeRules.CopyRuleName(copy, weapons[from].Name, texts);
            // an earlier export of the copy, in a source that is the mod's own export, gives way to the copy as the mod has it
            if (places.TryGetValue(copy.Descriptor, out int already)) weapons[already] = made;
            else
            {
                places[copy.Descriptor] = weapons.Count;
                weapons.Add(made);
            }
        }

        WithPictureSources(weapons, game.WeaponPictures ?? new Dictionary<string, string>());
        Dictionary<string, Weapon> byDescriptor = ByDescriptor(weapons);
        Dictionary<string, Unit> gameUnits = ByDescriptor(game.Units);
        var units = new List<Unit>(shown.Units.Count);
        var unitMarks = new List<(Unit, Unit, IReadOnlyCollection<string>, string)>();
        foreach (Unit unit in shown.Units)
        {
            Unit carrying = Carrying(unit, mod, byDescriptor, ruleNames.Keys, out List<string> rules);
            units.Add(carrying);
            if (ReferenceEquals(carrying, unit) || !gameUnits.TryGetValue(unit.Descriptor, out Unit? gameUnit)) continue;
            List<string> fields = [.. UnitChanges.FieldsChanged(unit, carrying)];
            unitMarks.Add((gameUnit, carrying, fields, string.Join(", ", rules.Select(descriptor => ruleNames[descriptor]))));
        }

        List<Weapon> withUsers = Weapon.WithUsersAmong(weapons, units);
        UnitCatalog catalog = shown with { Units = units, Weapons = withUsers };
        return new Result(catalog, WeaponMarks(mod, withUsers, ByDescriptor(shown.Weapons), gameWeapons, ruleNames), unitMarks);
    }

    /// <summary>
    /// A unit as it carries the mod's weapons: the copies given its loadout in place of their originals (a copy of a
    /// copy after the first), and the changed weapons with their new values. The same unit when none of its weapons changed.
    /// </summary>
    /// <param name="rules">The weapons (by descriptor) whose rules changed what the unit carries.</param>
    private static Unit Carrying(Unit unit, Mod mod, Dictionary<string, Weapon> weapons, IEnumerable<string> changedWeapons, out List<string> rules)
    {
        rules = [];
        var changed = new HashSet<string>(changedWeapons, StringComparer.Ordinal);
        var carried = new List<UnitWeapon>(unit.Weapons.Count);
        bool anyChanged = false;
        foreach (UnitWeapon weapon in unit.Weapons)
        {
            string ammunition = weapon.Ammunition;
            foreach (ModWeapon copy in mod.Weapons)
                if (copy.MadeFrom == ammunition && unit.WeaponSet is string loadout && copy.Loadouts.Contains(loadout)) ammunition = copy.Descriptor;
            if (!changed.Contains(ammunition) || !weapons.TryGetValue(ammunition, out Weapon? now))
            {
                carried.Add(weapon);
                continue;
            }
            carried.Add(now.ForUnit(weapon.Count));
            if (!rules.Contains(ammunition)) rules.Add(ammunition);
            anyChanged = true;
        }
        return anyChanged ? unit.WithWeapons(carried) : unit;
    }

    /// <summary>
    /// The marks of the weapons the Armory changes: each changed weapon's values, by its change's rule; each copy, as a
    /// weapon the mod adds; and the units carrying a weapon, where copies took some of them, by the copies' rules.
    /// </summary>
    private static List<(Weapon?, Weapon, IReadOnlyDictionary<string, string>)> WeaponMarks(Mod mod, List<Weapon> weapons,
        Dictionary<string, Weapon> beforeArmory, Dictionary<string, Weapon> gameWeapons, Dictionary<string, string> ruleNames)
    {
        var marks = new List<(Weapon?, Weapon, IReadOnlyDictionary<string, string>)>();
        foreach (Weapon weapon in weapons)
        {
            if (mod.Weapons.Any(copy => copy.Descriptor == weapon.Descriptor))
            {
                marks.Add((null, weapon, new Dictionary<string, string>()));
                continue;
            }
            if (!gameWeapons.TryGetValue(weapon.Descriptor, out Weapon? gameWeapon)) continue;
            var ruleByField = new Dictionary<string, string>(StringComparer.Ordinal);
            if (WeaponEdit.Of(mod, weapon.Descriptor) is WeaponEdit edit && ruleNames.TryGetValue(weapon.Descriptor, out string? rule))
                foreach (string field in edit.FieldsGiven()) ruleByField[field] = rule;
            bool usersChanged = beforeArmory.TryGetValue(weapon.Descriptor, out Weapon? before) && !before.Users.SequenceEqual(weapon.Users, StringComparer.Ordinal);
            if (usersChanged)
            {
                IEnumerable<string> copies = mod.Weapons.Where(copy => copy.MadeFrom == weapon.Descriptor && ruleNames.ContainsKey(copy.Descriptor)).Select(copy => ruleNames[copy.Descriptor]);
                ruleByField[WeaponField.Users] = string.Join(", ", copies);
            }
            if (ruleByField.Count > 0) marks.Add((gameWeapon, weapon, ruleByField));
        }
        return marks;
    }

    /// <summary>
    /// The pictures' files of the weapons given another weapon's picture, which have only its texture's name: the
    /// file the game's list gives the texture (<see cref="UnitCatalog.WeaponPictures"/>).
    /// </summary>
    private static void WithPictureSources(List<Weapon> weapons, IReadOnlyDictionary<string, string> sources)
    {
        for (int at = 0; at < weapons.Count; at++)
            if (weapons[at].PictureSource is null && weapons[at].PictureTexture is string texture && sources.TryGetValue(texture, out string? source))
                weapons[at] = weapons[at] with { PictureSource = source };
    }

    private static Dictionary<string, Weapon> ByDescriptor(IEnumerable<Weapon> weapons)
    {
        var all = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in weapons) all.TryAdd(weapon.Descriptor, weapon);
        return all;
    }

    private static Dictionary<string, Unit> ByDescriptor(IEnumerable<Unit> units)
    {
        var all = new Dictionary<string, Unit>(StringComparer.Ordinal);
        foreach (Unit unit in units) all.TryAdd(unit.Descriptor, unit);
        return all;
    }
}
