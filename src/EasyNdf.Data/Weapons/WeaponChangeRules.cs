using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
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
/// weapons change, are locked, and are run by the export like any rule.
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

    private readonly TextCatalog texts;
    private readonly JsonArray rules = [];

    private WeaponChangeRules(TextCatalog texts) => this.texts = texts;

    /// <summary>
    /// The rules for the mod's changes to the game's weapons, then for the weapons it adds, in the order it keeps them.
    /// The changes come first, so a weapon copied from a changed one is copied with the changes, as the Armory shows it.
    /// A change to a weapon the game does not have, and a copy of one, get none.
    /// </summary>
    /// <param name="mod">The mod.</param>
    /// <param name="game">The game's weapons, before the mod's changes.</param>
    /// <param name="texts">The language the rules are named in.</param>
    /// <param name="loadoutName">The names of the units with a loadout, by the loadout's descriptor, for the rules' names.</param>
    public static JsonArray For(Mod mod, IReadOnlyList<Weapon> game, TextCatalog texts, Func<string, string> loadoutName)
    {
        var byDescriptor = new Dictionary<string, Weapon>(StringComparer.Ordinal);
        foreach (Weapon weapon in game) byDescriptor.TryAdd(weapon.Descriptor, weapon);
        var writer = new WeaponChangeRules(texts);
        foreach (WeaponEdit edit in mod.WeaponEdits)
        {
            if (edit.ChangesNothing || !byDescriptor.TryGetValue(edit.Descriptor, out Weapon? gameWeapon)) continue;
            writer.Group(gameWeapon.Name);
            writer.EditRule(edit, gameWeapon);
        }
        // a copy may be made from an earlier copy, which is made by then
        var made = new HashSet<string>(byDescriptor.Keys, StringComparer.Ordinal);
        foreach (ModWeapon weapon in mod.Weapons)
        {
            if (!made.Contains(weapon.MadeFrom)) continue;
            made.Add(weapon.Descriptor);
            string madeFromName = byDescriptor.TryGetValue(weapon.MadeFrom, out Weapon? original) ? original.Name
                : mod.Weapons.FirstOrDefault(each => each.Descriptor == weapon.MadeFrom)?.Name ?? weapon.MadeFrom;
            writer.Group(weapon.Name);
            writer.CloneRule(weapon, madeFromName);
            foreach (string loadout in weapon.Loadouts) writer.LoadoutRule(weapon, loadout, loadoutName(loadout));
        }
        return writer.rules;
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
        ["Actions"] = ValueActions(edit),
    }, gameWeapon.File, edit.Descriptor, "Edit", EditChange);

    /// <summary>
    /// The copy's ammunition: a copy of its original, with new GUIDs, given its own values. Nothing else is touched:
    /// it keeps its original's picture, flash, sound, missile and the rest.
    /// </summary>
    private void CloneRule(ModWeapon weapon, string madeFromName) => Rule(new JsonObject
    {
        ["Name"] = CopyRuleName(weapon, madeFromName, texts),
        ["Kind"] = "Clone",
        ["Params"] = new JsonObject { ["Source"] = weapon.MadeFrom, ["NewName"] = weapon.Descriptor },
        ["Actions"] = ValueActions(weapon),
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
        ["Params"] = new JsonObject { ["Find"] = ReferencePrefix + weapon.MadeFrom, ["Replace"] = ReferencePrefix + weapon.Descriptor },
    }, LoadoutsFile, weapon.Descriptor, "Loadout", NewChange);

    /// <summary>The values given, each an action on the property of the ammunition the game keeps it in, written as the game writes it.</summary>
    private static JsonArray ValueActions(WeaponValues values)
    {
        var actions = new JsonArray();
        if (values.Picture is string picture) actions.Add(Action("InterfaceWeaponTexture", SetValue, "\"" + picture + "\""));
        foreach (WeaponStat stat in WeaponStat.All)
            if (values.Given(stat.Field) is double value) actions.Add(Action(stat.Target, SetValue, stat.Written(value)));
        foreach (WeaponChoice choice in WeaponChoice.All)
            if (values.Chosen(choice.Field) is string written) actions.Add(Action(choice.Field, SetValue, written));
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
