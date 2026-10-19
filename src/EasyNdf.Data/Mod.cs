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
        Presets = [.. Presets.Select(p => new ModPreset { Name = p.Name, Rules = (JsonArray)p.Rules.DeepClone() })],
        Divisions = [.. Divisions.Select(division => Data.Divisions.ModDivision.FromJson(division.ToJson()))],
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
