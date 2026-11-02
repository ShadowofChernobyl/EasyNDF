using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Languages;
using EasyNdf.Engine.Rules;
using static EasyNdf.Data.Divisions.DivisionChangeRules;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The mod's weapons as ordinary rules, for its "Weapons" Armory preset, under a group for each weapon. First its
/// changes to the game's own weapons: an Edit rule giving the weapon's ammunition the values the user changed. Then the
/// weapons it adds: a Clone rule that copies the original ammunition in its file and gives the copy its own values, and
/// for each loadout given the copy, a Replace rule pointing that loadout (WeaponDescriptor.ndf) at the copy in place of
/// the original. Like the units' rules (<see cref="Units.UnitChangeRules"/>), they are made again whenever the mod's
/// weapons change, are locked, and are run by the export like any rule. Each game's rules use its own properties and
/// forms: Steel Division 2's (<see cref="SteelDivisionAmmunition"/>) are written for its mods only, and what it lacks
/// is left out, never written WARNO's way.
/// </summary>
public sealed class WeaponChangeRules
{
    /// <summary>The key, under <see cref="DivisionChangeRules.ArmoryKey"/>, of the weapon a rule is for, by its ammunition's descriptor.</summary>
    public const string WeaponKey = "Weapon";

    /// <summary>The file the units' loadouts are in, which a copy's Replace rules change.</summary>
    public const string LoadoutsFile = "WeaponDescriptor.ndf";

    /// <summary>Where the game keeps its ammunition, as the loadouts name it: "$/GFX/Weapon/Ammo_X".</summary>
    public const string ReferencePrefix = "$/GFX/Weapon/";

    /// <summary>
    /// What the Armory keeps of a rule (its <see cref="DivisionChangeRules.ChangeKey"/>): "New" for the rules of a
    /// weapon the mod adds, "Edit" for those of its changes to one of the game's weapons.
    /// </summary>
    public const string NewChange = "New", EditChange = "Edit";

    /// <summary>The operator that sets a single value, such as a number.</summary>
    private const string SetValue = "Set To [int, float, string, boolean]";

    /// <summary>The operator that sets a whole list, such as the traits.</summary>
    private const string SetList = "Set To [list, object]";

    /// <summary>
    /// The games whose every Armory change to weapons is written as rules: changes to the game's weapons, and new
    /// weapons (copies) given to units' loadouts. The other games get only the changes to their own weapons' values
    /// that <see cref="Writes(GameTitle, string)"/> allows: Steel Division 2's loadouts are laid out otherwise
    /// (SalvoStockIndex, Salves padded with -1, "~/" references; see the notes, section 3.1), and a copy is of no use
    /// to a unit that cannot be given it.
    /// </summary>
    private static readonly IReadOnlyList<GameTitle> GamesWrittenInFull = [GameTitle.Warno];

    /// <summary>
    /// Whether the Armory can write a change to one of a weapon's values (<see cref="WeaponField"/>, or a property kept
    /// as written) for a game: any for WARNO; for Steel Division 2, those its ammunition has a property for.
    /// </summary>
    public static bool Writes(GameTitle game, string field) =>
        GamesWrittenInFull.Contains(game) || (game == GameTitle.SteelDivision2 && SteelDivisionAmmunition.Of(field) is not null);

    /// <summary>
    /// Whether the Armory can write a change to one of this weapon's values for a game: as <see cref="Writes(GameTitle, string)"/>,
    /// and for Steel Division 2's level of damage, only a weapon whose kind of damage has one (AP).
    /// </summary>
    public static bool Writes(GameTitle game, string field, Weapon weapon)
    {
        if (!Writes(game, field)) return false;
        if (GamesWrittenInFull.Contains(game) || SteelDivisionAmmunition.Of(field)?.Form != SteelDivisionAmmunition.Form.DamageLevel) return true;
        return SteelDivisionAmmunition.HasLevel(weapon);
    }

    /// <summary>Whether the Armory can add weapons (copies of the game's) to a mod of a game, and give them to units.</summary>
    public static bool AddsWeapons(GameTitle game) => GamesWrittenInFull.Contains(game) || game == GameTitle.SteelDivision2;

    /// <summary>
    /// Whether a game keeps a weapon's level of damage inside its kind of damage, as Steel Division 2's
    /// "EArmeType/AP_26" does; WARNO keeps them apart (Family and Index).
    /// </summary>
    public static bool KeepsLevelInDamageType(GameTitle game) => game == GameTitle.SteelDivision2;

    /// <summary>
    /// For a game that keeps a weapon's level of damage inside its kind of damage (<see cref="KeepsLevelInDamageType"/>),
    /// the kind of damage a weapon is given with a new level, as the file writes it, which the Armory then keeps as the
    /// choice of the kind of damage, so the two are never written apart; null for a game that keeps them apart, or a
    /// weapon whose damage has no level.
    /// </summary>
    public static string? LevelAsDamageType(GameTitle game, Weapon weapon, int level) =>
        KeepsLevelInDamageType(game) ? SteelDivisionAmmunition.DamageTypeWithLevel(weapon, level) : null;

    /// <summary>
    /// The property of a game's ammunition a value is in, as the Weapon pane names it: Steel Division 2's own
    /// ("PorteeMaximale" for the range), or null for a value it does not have; for WARNO, <paramref name="warnoProperty"/>.
    /// </summary>
    public static string? PropertyIn(GameTitle game, string field, string warnoProperty) =>
        game == GameTitle.SteelDivision2 ? SteelDivisionAmmunition.Of(field)?.Name : warnoProperty;

    private readonly TextCatalog texts;
    private readonly GameTitle game;
    private readonly JsonArray rules = [];

    private WeaponChangeRules(TextCatalog texts, GameTitle game)
    {
        this.texts = texts;
        this.game = game;
    }

    /// <summary>
    /// The rules for the mod's changes to the game's weapons, then for the weapons it adds, in the order it keeps them.
    /// The changes come first, so a weapon copied from a changed one is copied with the changes, as the Armory shows it.
    /// A change to a weapon the game does not have, and a copy of one, get none; nor do the weapons added to a mod of a
    /// game the Armory does not add weapons to (<see cref="AddsWeapons"/>).
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's weapons, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="loadoutName">The names of the units with a loadout, by the loadout's descriptor, for the rules' names.</param>
    public static JsonArray For(Mod mod, IReadOnlyList<Weapon> game, TextCatalog texts, Func<string, string> loadoutName)
    {
        var byDescriptor = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in game) byDescriptor.TryAdd(weapon.Descriptor, weapon);
        var writer = new WeaponChangeRules(texts, mod.Game);
        foreach (WeaponEdit edit in mod.WeaponEdits)
        {
            if (edit.ChangesNothing || !byDescriptor.TryGetValue(edit.Descriptor, out Weapon? gameWeapon)) continue;
            // what the Armory cannot write for the mod's game is left out, never written WARNO's way
            if (writer.ValueActions(edit, gameWeapon).Count == 0) continue;
            writer.Group(gameWeapon.Name);
            writer.EditRule(edit, gameWeapon);
        }
        if (!AddsWeapons(mod.Game)) return writer.rules;
        // a copy may be made from an earlier copy, which is made by then
        var made = new HashSet<string>(byDescriptor.Keys, StringComparer.Ordinal);
        foreach (ModWeapon weapon in mod.Weapons)
        {
            if (!made.Contains(weapon.MadeFrom)) continue;
            made.Add(weapon.Descriptor);
            string madeFromName = byDescriptor.TryGetValue(weapon.MadeFrom, out Weapon? original) ? original.Name
                : mod.Weapons.FirstOrDefault(each => each.Descriptor == weapon.MadeFrom)?.Name ?? weapon.MadeFrom;
            writer.Group(weapon.Name);
            writer.CloneRule(weapon, madeFromName, GameWeaponOf(weapon, mod, byDescriptor));
            foreach (string loadout in weapon.Loadouts) writer.LoadoutRule(weapon, loadout, loadoutName(loadout));
        }
        return writer.rules;
    }

    /// <summary>
    /// The game's weapon a copy is made from, through the copies it may be made from in turn: its scale and kind of
    /// damage are the copy's. Null when none is the game's.
    /// </summary>
    private static Weapon? GameWeaponOf(ModWeapon copy, Mod mod, IReadOnlyDictionary<string, Weapon> game)
    {
        string from = copy.MadeFrom;
        for (int step = 0; step <= mod.Weapons.Count; step++)
        {
            if (game.TryGetValue(from, out Weapon? weapon)) return weapon;
            from = mod.Weapons.FirstOrDefault(each => each.Descriptor == from)?.MadeFrom ?? "";
        }
        return null;
    }

    /// <summary>The name of a change's rule, which the Armory's marks name: "Canon AP 120mm: changed in the Armory".</summary>
    public static string EditRuleName(Weapon gameWeapon, TextCatalog texts) => gameWeapon.Name + ": " + texts.Get("Weapons.Rule.Changed");

    /// <summary>The name of a copy's rule, which the Armory's marks name: "M256 DU: copy of Canon AP 120mm".</summary>
    public static string CopyRuleName(ModWeapon weapon, string madeFromName, TextCatalog texts) =>
        weapon.Name + ": " + texts.Get("Weapons.Rule.Copy", ("weapon", madeFromName));

    /// <summary>A change to one of the game's weapons: an Edit rule on its ammunition giving it the values the user changed.</summary>
    private void EditRule(WeaponEdit edit, Weapon gameWeapon) => Rule(new JsonObject
    {
        ["Name"] = EditRuleName(gameWeapon, texts),
        ["Kind"] = "Edit",
        ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, edit.Descriptor)),
        ["Actions"] = ValueActions(edit, gameWeapon),
    }, gameWeapon.File, edit.Descriptor, "Edit", EditChange);

    /// <summary>
    /// The copy's ammunition: a copy of its original, with new GUIDs (Steel Division 2's hit roll has its own too), given
    /// its own values in the game's forms. Nothing else is touched: it keeps its original's picture, flash, sound,
    /// missile and the rest.
    /// </summary>
    /// <param name="gameWeapon">The game's weapon it is made from, for Steel Division 2's scale and kind of damage.</param>
    private void CloneRule(ModWeapon weapon, string madeFromName, Weapon? gameWeapon) => Rule(new JsonObject
    {
        ["Name"] = CopyRuleName(weapon, madeFromName, texts),
        ["Kind"] = "Clone",
        ["Params"] = new JsonObject { ["Source"] = weapon.MadeFrom, ["NewName"] = weapon.Descriptor },
        ["Actions"] = game == GameTitle.SteelDivision2 && gameWeapon is not null ? SteelDivisionValueActions(weapon, gameWeapon) : WarnoValueActions(weapon),
    }, weapon.File, weapon.Descriptor, "Clone", NewChange);

    /// <summary>
    /// A loadout given the copy: every place it names the original ammunition names the copy instead. Only that
    /// loadout's descriptor is changed, so the other units keep the original.
    /// </summary>
    private void LoadoutRule(ModWeapon weapon, string loadout, string unitNames) => Rule(new JsonObject
    {
        ["Name"] = weapon.Name + ": " + texts.Get("Weapons.Rule.Loadout", ("units", unitNames)),
        ["Kind"] = "Replace",
        ["Conditions"] = new JsonArray(Condition(Conditions.DescriptorTitle, loadout)),
        ["Params"] = new JsonObject
        {
            ["Find"] = LoadoutRules.AmmunitionReference(weapon.MadeFrom, game), ["Replace"] = LoadoutRules.AmmunitionReference(weapon.Descriptor, game),
        },
    }, LoadoutsFile, weapon.Descriptor, "Loadout", NewChange);

    /// <summary>
    /// The values given, each an action on the property of the ammunition the game the rules are for keeps it in,
    /// written as that game writes it (<see cref="WarnoValueActions"/>, <see cref="SteelDivisionValueActions"/>).
    /// </summary>
    /// <param name="values">The values given.</param>
    /// <param name="gameWeapon">The weapon they are given to, as the game has it: its scale, and its kind of damage.</param>
    private JsonArray ValueActions(WeaponValues values, Weapon gameWeapon) =>
        game == GameTitle.SteelDivision2 ? SteelDivisionValueActions(values, gameWeapon) : WarnoValueActions(values);

    /// <summary>WARNO's actions for the values given, on its ammunition's properties, written as WARNO writes them.</summary>
    private static JsonArray WarnoValueActions(WeaponValues values)
    {
        var actions = new JsonArray();
        if (values.Picture is string picture) actions.Add(Action("InterfaceWeaponTexture", SetValue, "\"" + picture + "\""));
        // the traits as the game writes them: ['MOTION', 'F&F', 'HEAT']
        if (values.Traits is IReadOnlyList<string> traits)
            actions.Add(Action("TraitsToken", SetList, "[" + string.Join(", ", traits.Select(token => "'" + token + "'")) + "]"));
        foreach (WeaponStat stat in WeaponStat.All)
            if (values.Given(stat.Field) is double value) actions.Add(Action(stat.Target, SetValue, stat.Written(value)));
        foreach (WeaponChoice choice in WeaponChoice.All)
            if (values.Chosen(choice.Field) is string written) actions.Add(Action(choice.Field, SetValue, written));
        return actions;
    }

    /// <summary>
    /// Steel Division 2's actions for the values given, as its Ammunition.ndf writes them (<see cref="SteelDivisionAmmunition"/>):
    /// each on its own property, found by name wherever it is in the ammunition (its hit roll's chances to hit by their
    /// key), with numbers in the files' forms: distances as ((N) * Metre) in the files' own units, the level of damage
    /// inside the kind of damage ("EArmeType/AP_30"). The picture is written as WARNO's is; the traits, and the values
    /// its ammunition does not have, are not written at all. A level is written before the kind of damage chosen, so a
    /// kind chosen wins, as the Armory shows it.
    /// </summary>
    private static JsonArray SteelDivisionValueActions(WeaponValues values, Weapon gameWeapon)
    {
        var actions = new JsonArray();
        if (values.Picture is string picture && SteelDivisionAmmunition.Of(WeaponField.Picture) is SteelDivisionAmmunition.Property texture)
            actions.Add(Action(texture.Name, SetValue, "\"" + picture + "\""));
        foreach (WeaponStat stat in WeaponStat.All)
        {
            if (values.Given(stat.Field) is not double value || SteelDivisionAmmunition.Of(stat.Field) is not SteelDivisionAmmunition.Property property) continue;
            if (SteelDivisionAmmunition.Written(property, value, gameWeapon) is string written) actions.Add(Action(property.Name, SetValue, written));
        }
        foreach (WeaponChoice choice in WeaponChoice.All)
            if (values.Chosen(choice.Field) is string written && SteelDivisionAmmunition.Of(choice.Field) is SteelDivisionAmmunition.Property property)
                actions.Add(Action(property.Name, SetValue, written));
        return actions;
    }

    // ---- making rules ----

    /// <summary>A group heading for a weapon's rules.</summary>
    private void Group(string weaponName) => rules.Add(new JsonObject
    {
        ["Name"] = weaponName,
        ["Kind"] = "Group",
        ["Enabled"] = true,
        ["LogicGate"] = "All",
        ["Conditions"] = new JsonArray(),
        ["Actions"] = new JsonArray(),
        ["Params"] = new JsonObject(),
        [ArmoryKey] = new JsonObject { [ShowsKey] = "Group" },
    });

    /// <summary>Adds a rule with what every rule has, and what the Armory knows of it.</summary>
    private void Rule(JsonObject rule, string file, string descriptor, string shows, string change)
    {
        rule["Enabled"] = true;
        rule["LogicGate"] = "All";
        rule["Conditions"] ??= new JsonArray();
        rule["Actions"] ??= new JsonArray();
        rule["Params"] ??= new JsonObject();
        // a weapon's rules go together: a loadout pointed at a copy never made would leave its unit without the weapon
        rule[ArmoryKey] = new JsonObject { [FileKey] = file, [WeaponKey] = descriptor, [ShowsKey] = shows, [ChangeKey] = change, [LockedKey] = true };
        rules.Add(rule);
    }
}
