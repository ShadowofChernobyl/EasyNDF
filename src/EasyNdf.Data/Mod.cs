using System.Text.Json.Nodes;

namespace EasyNdf.Data;

/// <summary>A mod: a name, its presets, and its watermark settings.</summary>
public sealed class Mod
{
    /// <summary>The mod's name, as shown in the Mod dropdown and written into its files.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The game the mod is for: it runs only on files of that game, and the Armory saves into it only changes made with
    /// a source of that game. Mods made before mods had a game, and those of other apps, are WARNO's.
    /// </summary>
    public Game.GameTitle Game { get; set; } = Data.Game.GameTitle.Warno;

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
    /// The length of the mod file's text the mod was read from; 0 for a mod made otherwise (a copy, one from a backup).
    /// Rules read from a file keep the whole file in memory while any of them is in use, for the usage readout.
    /// </summary>
    public long ReadFromCharacters { get; set; }

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
    /// <param name="packRules">Whether the copy's rules are kept packed (<see cref="ModPreset.Copy"/>), as the Undo
    /// history keeps them; a packed copy is copied again unpacked, and stays packed itself.</param>
    /// <param name="sameRulesAs">A packed copy whose rule sets hold the same rules, in the same order: their packed
    /// rules are shared rather than packed again.</param>
    public Mod DeepCopy(bool packRules = false, Mod? sameRulesAs = null) => new()
    {
        Name = Name,
        Game = Game,
        Watermark = Watermark,
        Presets = [.. Presets.Select((p, i) => p.Copy(packRules, sameRulesAs?.Presets.ElementAtOrDefault(i)))],
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
    /// <see cref="EasyNdf.Engine.Mods.RuleLoading.LoadForEngine"/> turns them into rules the engine runs. A large rule
    /// set put away with <see cref="PackRules"/> is unpacked here the first time it is read again: to show how many
    /// rules it has, read <see cref="RuleCount"/> instead.
    /// </summary>
    public JsonArray Rules
    {
        get
        {
            lock (packing)
            {
                if (rules is null)
                {
                    rules = JsonNode.Parse(packedRules!.Unpack()) as JsonArray ?? [];
                    packedRules = null;
                }
                return rules;
            }
        }
        set
        {
            lock (packing)
            {
                rules = value;
                packedRules = null;
                RulesVersion++;
            }
        }
    }

    /// <summary>
    /// Counts each time <see cref="Rules"/> is given other rules, and never when they are put away or unpacked, which
    /// make other JSON objects of the same rules: what Undo looks at to tell whether the rules were swapped.
    /// </summary>
    public int RulesVersion { get; private set; }

    /// <summary>
    /// The rules to look at without changing them: while put away, a copy read from the packed text, the set itself
    /// staying put away (and its <see cref="PackedRules"/> the same, for what is worked out from them to be kept).
    /// </summary>
    public JsonArray RulesToRead()
    {
        lock (packing)
            return rules ?? JsonNode.Parse(packedRules!.Unpack()) as JsonArray ?? [];
    }

    /// <summary>The rules as JSON text, as <see cref="Rules"/> writes it, read from the packed text while put away rather than unpacked.</summary>
    public string RulesText()
    {
        lock (packing)
            return rules is not null ? JsonFiles.TextOf(rules) : System.Text.Encoding.UTF8.GetString(packedRules!.Unpack());
    }

    private JsonArray? rules = [];

    /// <summary>The rules as packed JSON text while they are put away; null while they are in use.</summary>
    private FileList.PackedBytes? packedRules;

    private readonly object packing = new();

    /// <summary>How many rules the packed rules hold, and how many of them are not group headings.</summary>
    private (int All, int NotGroups) packedCounts;

    /// <summary>
    /// Puts the rules away while nothing uses them (Paul's choices: option C of the memory report, then every rule set
    /// whatever its size): kept as JSON objects, a mod's thousands of Armory rules took some 70 MB, and rules read from
    /// a mod file keep that whole file while any of the mod's are in use (WARNO ACTUAL's, 71 MB); packed text takes a
    /// few hundred KB. The next read of <see cref="Rules"/> unpacks them, in some tens of milliseconds. Only call it
    /// while no rule of the set is held elsewhere (shown on the Rules tab, open in a rule window), as a change made to
    /// one held would be lost.
    /// </summary>
    public void PackRules()
    {
        lock (packing)
        {
            if (rules is null) return;
            packedCounts = Counts(rules);
            packedRules = FileList.PackedBytes.Pack(JsonFiles.Utf8Of(rules));
            rules = null;
        }
    }

    /// <summary>How many rules there are, without unpacking them.</summary>
    public int RuleCount
    {
        get { lock (packing) return rules?.Count ?? packedCounts.All; }
    }

    /// <summary>How many rules there are, group headings left out, without unpacking them.</summary>
    public int RuleCountWithoutGroups
    {
        get { lock (packing) return rules is not null ? Counts(rules).NotGroups : packedCounts.NotGroups; }
    }

    private static (int All, int NotGroups) Counts(JsonArray rules) =>
        (rules.Count, rules.Count(rule => (string?)rule?["Kind"] != "Group"));

    /// <summary>
    /// A copy that shares nothing with it but packed rules, which never change. Packed, its rules are kept as packed
    /// text whatever their number: the Undo history keeps each step so, at about a twentieth of what the rules take as
    /// JSON objects. Unpacked, its rules are read from this one's, leaving this one as it is, packed or not.
    /// </summary>
    /// <param name="packed">Whether the copy's rules are kept packed.</param>
    /// <param name="sameRulesAs">A packed copy holding the same rules, whose packed rules the copy shares.</param>
    public ModPreset Copy(bool packed, ModPreset? sameRulesAs = null)
    {
        var copy = new ModPreset { Name = Name, ArmoryPart = ArmoryPart, AppliesTo = AppliesTo is null ? null : [.. AppliesTo] };
        FileList.PackedBytes? shared = null;
        if (packed && sameRulesAs is not null) lock (sameRulesAs.packing) shared = sameRulesAs.packedRules;
        lock (packing)
        {
            if (packed)
            {
                copy.packedCounts = rules is not null ? Counts(rules) : packedCounts;
                copy.packedRules = shared ?? packedRules ?? FileList.PackedBytes.Pack(JsonFiles.Utf8Of(rules!));
                copy.rules = null;
            }
            else copy.rules = rules is not null ? (JsonArray)rules.DeepClone() : JsonNode.Parse(packedRules!.Unpack()) as JsonArray ?? [];
        }
        return copy;
    }

    /// <summary>The size of the rules while put away (<see cref="PackRules"/>), packed; null while they are in use.</summary>
    public long? PackedLength
    {
        get { lock (packing) return packedRules?.PackedLength; }
    }

    /// <summary>The rules while put away, packed; null while they are in use.</summary>
    public FileList.PackedBytes? PackedRules
    {
        get { lock (packing) return packedRules; }
    }

    /// <summary>Whether the rules are put away (<see cref="PackRules"/>).</summary>
    public bool IsPacked
    {
        get { lock (packing) return rules is null; }
    }

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
