using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>A mod: a name, its presets, and its watermark settings.</summary>
public sealed class Mod
{
    /// <summary>The mod's name, as shown in the Mod dropdown and written into its files.</summary>
    public string Name { get; set; } = "";

    /// <summary>The mod's watermark settings, or null when they were never set for this mod.</summary>
    public ModWatermark? Watermark { get; set; }

    /// <summary>The mod's presets. Each preset's rules apply to the files it is chosen for.</summary>
    public List<ModPreset> Presets { get; set; } = [];

    /// <summary>The mod's divisions: its new ones, and its changes to the game's, made on the Divisions tab.</summary>
    public List<Divisions.ModDivision> Divisions { get; set; } = [];

    /// <summary>The units the mod adds, made in the Armory: new ones and duplicates.</summary>
    public List<Units.ModUnit> Units { get; set; } = [];

    /// <summary>The mod's changes to the game's own units, made in the Armory's Unit pane.</summary>
    public List<Units.UnitEdit> UnitEdits { get; set; } = [];

    /// <summary>The mod's changes to the game's own weapons, made in the Armory's Weapon pane.</summary>
    public List<Weapons.WeaponEdit> WeaponEdits { get; set; } = [];

    /// <summary>The weapons the mod adds, made in the Armory: copies of the game's (or of its own), for chosen units.</summary>
    public List<Weapons.ModWeapon> Weapons { get; set; } = [];

    /// <summary>
    /// The numbers for deck codes the mod gives units that have none, by descriptor: units its rules add (Clone rules)
    /// that its divisions use. The game refuses a deck with a unit missing from DeckSerializer.ndf; a number once given
    /// is kept, so decks shared as codes stay the same from one export to the next.
    /// </summary>
    public SortedDictionary<string, int> UnitDeckNumbers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether the mod was switched on on the Mods tab when it was exported, as its mod file says ("On"), so an import
    /// keeps it; null when the file doesn't say, as one made before mods could run together or by another app. Only
    /// a mod just read from a file has it: the app keeps the switch with its own state.
    /// </summary>
    public bool? WasOn { get; set; }

    /// <summary>
    /// The files of the mod's File rules that its mod file carried, by the name they are kept under, for the import to
    /// put into the app's <see cref="ModFileStore"/>. Only a mod just read from a file has them.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> CarriedFiles { get; set; } = new Dictionary<string, byte[]>();

    /// <summary>
    /// A preset name in this mod that no other preset of the mod has: the name itself, or "name (2)", "name (3)"...
    /// </summary>
    public string FreePresetName(string name)
    {
        if (!HasPreset(name)) return name;
        int i = 2;
        while (HasPreset(name + " (" + i + ")")) i++;
        return name + " (" + i + ")";
    }

    private bool HasPreset(string name) => Presets.Any(p => p.Name == name);

    /// <summary>A copy of the mod that shares nothing with it, so changing one leaves the other as it was.</summary>
    public Mod DeepCopy() => new()
    {
        Name = Name,
        Watermark = Watermark,
        Presets = [.. Presets.Select(p => new ModPreset { Name = p.Name, Rules = (JsonArray)p.Rules.DeepClone(), ArmoryPart = p.ArmoryPart, AppliesTo = p.AppliesTo is null ? null : [.. p.AppliesTo] })],
        Divisions = [.. Divisions.Select(division => Data.Divisions.ModDivision.FromJson(division.ToJson()))],
        Units = [.. Units.Select(unit => unit.Copy())],
        UnitEdits = [.. UnitEdits.Select(edit => edit.Copy())],
        WeaponEdits = [.. WeaponEdits.Select(edit => edit.Copy())],
        Weapons = [.. Weapons.Select(weapon => weapon.Copy())],
        UnitDeckNumbers = new(UnitDeckNumbers, StringComparer.Ordinal),
    };
}

/// <summary>A preset: a name and a list of rules, in the mod file format.</summary>
public sealed class ModPreset
{
    /// <summary>The preset's name. Within one mod no two presets share a name.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The rules, kept as the JSON of the mod file format so that fields this version does not know about are kept too.
    /// <see cref="EasyNdf.Engine.Mods.RuleLoading.LoadForEngine"/> turns them into rules the engine runs.
    /// </summary>
    public JsonArray Rules { get; set; } = [];

    /// <summary>
    /// The part of the Armory whose changes the preset keeps as rules, made again whenever these change (see
    /// <see cref="Divisions.ArmoryPresets"/>); null for a preset of the user's own.
    /// </summary>
    public ArmoryPart? ArmoryPart { get; set; }

    /// <summary>Whether it is one of the mod's Armory presets, listed under Armory Changes on the Rules tab.</summary>
    public bool IsArmoryPreset => ArmoryPart is not null;

    /// <summary>
    /// The files the rule set applies to, as the user listed them in its Applies To window: names such as
    /// "Ammunition.ndf", or with folders before them, "Strategic/GDConstants.ndf", to pick one of two files of the same
    /// name. Null when it applies to the files its own name holds, as rule sets always did (see
    /// <see cref="FileList.RuleSetFiles"/>).
    /// </summary>
    public List<string>? AppliesTo { get; set; }
}

/// <summary>
/// The Armory's parts, its sub-tabs: Units (every unit), Divisions (a division's units and details), Battlegroups
/// (a division's Official Battlegroup) and Weapons (every weapon). A mod keeps the changes made in each in an Armory
/// preset of its own.
/// </summary>
public enum ArmoryPart
{
    /// <summary>Every unit.</summary>
    Units,

    /// <summary>A division's units and details.</summary>
    Divisions,

    /// <summary>A division's Official Battlegroup.</summary>
    Battlegroups,

    /// <summary>Every weapon: the ammunition the units carry.</summary>
    Weapons,
}

/// <summary>
/// A mod's watermark: the line written at the top of exported files naming who made the mod.
/// In the files it is <c>{"on", "author", "ver", "game", "ask"}</c>, as in the HTML app.
/// </summary>
public sealed record ModWatermark(bool On, string Author, string Version, string Game, bool Ask)
{
    /// <summary>Texts longer than this are cut, as in the HTML app.</summary>
    private const int MaximumLength = 120;

    /// <summary>
    /// Reads a watermark from mod file JSON, the way the HTML app's <c>wmSet</c> does: line breaks become spaces, long
    /// texts are cut, "on" is anything JavaScript counts as true, and "ask" is on unless it is false.
    /// </summary>
    public static ModWatermark FromJson(JsonObject json) => new(
        On: JsonValues.IsTruthy(json["on"]),
        Author: Clip(json["author"]),
        Version: Clip(json["ver"]),
        Game: Clip(json["game"]),
        Ask: !JsonValues.IsFalse(json["ask"]));

    /// <summary>The watermark as mod file JSON.</summary>
    public JsonObject ToJson() => new()
    {
        ["on"] = On,
        ["author"] = Author,
        ["ver"] = Version,
        ["game"] = Game,
        ["ask"] = Ask,
    };

    private static string Clip(JsonNode? value)
    {
        string text = string.Join(" ", JsonValues.AsText(value).Split(["\r\n", "\r", "\n"], StringSplitOptions.None));
        return text.Length > MaximumLength ? text[..MaximumLength] : text;
    }
}
