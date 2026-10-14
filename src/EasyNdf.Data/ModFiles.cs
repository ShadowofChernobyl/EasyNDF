using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Text;

namespace EasyNdf.Data;

/// <summary>
/// The mod file format (<c>FormatVersion: 1</c>), shared with the HTML app: "Export mod", "Back up all mods", and reading
/// either kind back in.
/// </summary>
public static class ModFiles
{
    /// <summary>
    /// The text "Export mod" saves: <c>{ModName, FormatVersion, Watermark?, Presets: [{PresetName, Rules}]}</c>.
    /// Conditions and actions without a name get one made from what they do, as in the HTML app.
    /// </summary>
    public static string ExportText(Mod mod) => JsonFiles.ToText(ToJson(mod, nameRows: true));

    /// <summary>
    /// The mod as stored in the Mods folder: the same format as "Export mod", but rows without a name stay without one,
    /// so the Rules tab keeps showing them as they are.
    /// </summary>
    public static string StoredText(Mod mod) => JsonFiles.ToText(ToJson(mod, nameRows: false));

    /// <summary>
    /// The text "Back up all mods" saves: a list of every preset, each naming its mod. The first preset of each mod also
    /// carries the mod's watermark; versions before 0.71.0 ignore it. Mods without presets are not in it, as in the HTML app.
    /// </summary>
    public static string BackupText(IEnumerable<Mod> mods)
    {
        var all = new JsonArray();
        foreach (Mod mod in mods)
        {
            for (int i = 0; i < mod.Presets.Count; i++)
            {
                var entry = new JsonObject
                {
                    ["PresetName"] = mod.Presets[i].Name,
                    ["Mod"] = mod.Name,
                    ["Rules"] = mod.Presets[i].Rules.DeepClone(),
                };
                if (i == 0 && mod.Watermark is not null) entry["Watermark"] = mod.Watermark.ToJson();
                all.Add(entry);
            }
        }
        return JsonFiles.ToText(all);
    }

    /// <summary>
    /// Reads a mod file or a backup. Returns its mods, with their rules tidied as they load.
    /// Throws <see cref="ModFileException"/>, with the reason the HTML app gives, for anything else.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="fileName">The file's name: a mod file without a ModName is named after it.</param>
    public static List<Mod> Read(string text, string fileName)
    {
        JsonNode? json;
        try { json = JsonFiles.Parse(text); }
        catch (JsonException error) { throw new ModFileException("not valid JSON: " + error.Message); }

        if (json is JsonObject modFile && modFile["Presets"] is JsonArray presets)
            return [ReadModFile(modFile, presets, NameFromFileName(fileName))];
        if (json is JsonArray list && IsBackup(list))
            return ReadBackup(list);
        if (LooksLikePresets(json))
            throw new ModFileException("a preset, not a mod. Open the file, copy its text, and paste it into the preset list");
        throw new ModFileException("not a mod: a mod file holds a \"Presets\" list");
    }

    private static Mod ReadModFile(JsonObject modFile, JsonArray presets, string fileBaseName)
    {
        string name = JsonValues.IsTruthy(modFile["ModName"]) ? JsText.Trim(JsonValues.AsText(modFile["ModName"])) : "";
        if (name == "") name = fileBaseName;
        Mod mod = ModWithPresets(name, presets);
        if (modFile["Watermark"] is JsonObject watermark) mod.Watermark = ModWatermark.FromJson(watermark);
        return mod;
    }

    /// <summary>A backup's presets, grouped into their mods in the order the mods first appear.</summary>
    private static List<Mod> ReadBackup(JsonArray list)
    {
        var byMod = new Dictionary<string, List<JsonObject>>();
        var order = new List<string>();
        foreach (JsonObject entry in list.Cast<JsonObject>())
        {
            string name = JsText.Trim(JsonValues.AsText(entry["Mod"]));
            if (!byMod.ContainsKey(name)) { byMod[name] = []; order.Add(name); }
            byMod[name].Add(entry);
        }

        var mods = new List<Mod>();
        foreach (string name in order)
        {
            Mod mod = ModWithPresets(name, new JsonArray(byMod[name].Select(e => (JsonNode)e.DeepClone()).ToArray()));
            JsonObject? withWatermark = byMod[name].FirstOrDefault(e => e["Watermark"] is JsonObject);
            if (withWatermark is not null) mod.Watermark = ModWatermark.FromJson((JsonObject)withWatermark["Watermark"]!);
            mods.Add(mod);
        }
        return mods;
    }

    /// <summary>
    /// A mod holding the given presets. Every preset must have a "Rules" list. A preset without a name is called
    /// "Mod 1", "Mod 2"... by its place, and a name used twice gets " (2)".
    /// </summary>
    private static Mod ModWithPresets(string modName, JsonArray presets)
    {
        for (int i = 0; i < presets.Count; i++)
            if (presets[i] is not JsonObject preset || preset["Rules"] is not JsonArray)
                throw new ModFileException("preset " + (i + 1) + " in it has no \"Rules\" list");

        var mod = new Mod { Name = modName };
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = (JsonObject)presets[i]!;
            string presetName = JsText.Trim(JsonValues.AsText(preset["PresetName"]));
            if (presetName == "") presetName = modName + " " + (i + 1);
            mod.Presets.Add(new ModPreset { Name = mod.FreePresetName(presetName), Rules = RuleLoading.LoadRules(preset["Rules"]) });
        }
        return mod;
    }

    /// <summary>A backup is a list of presets that each name their mod.</summary>
    private static bool IsBackup(JsonArray list) =>
        list.Count > 0 && list.All(x => x is JsonObject entry && entry["Rules"] is JsonArray && JsText.Trim(JsonValues.AsText(entry["Mod"])) != "");

    /// <summary>Whether the file holds a preset or rules instead of a mod: a common mistake, with its own message.</summary>
    private static bool LooksLikePresets(JsonNode? json)
    {
        if (json is JsonObject single && single["Rules"] is JsonArray) return true;
        return json is JsonArray list && list.Count > 0 && list.All(x =>
            x is JsonObject entry && (entry["Rules"] is JsonArray || JsonValues.IsTruthy(entry["Conditions"]) || JsonValues.IsTruthy(entry["Actions"])));
    }

    /// <summary>"WTO.mod.json" and "WTO.json" both give "WTO".</summary>
    private static string NameFromFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        foreach (string ending in new[] { ".mod.json", ".json" })
            if (name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) return name[..^ending.Length];
        return name;
    }

    private static JsonObject ToJson(Mod mod, bool nameRows)
    {
        var json = new JsonObject { ["ModName"] = mod.Name, ["FormatVersion"] = 1 };
        if (mod.Watermark is not null) json["Watermark"] = mod.Watermark.ToJson();
        json["Presets"] = new JsonArray(mod.Presets.Select(p => (JsonNode)new JsonObject
        {
            ["PresetName"] = p.Name,
            ["Rules"] = nameRows ? RulesWithRowNames(p.Rules) : p.Rules.DeepClone(),
        }).ToArray());
        return json;
    }

    /// <summary>
    /// A copy of the rules in which every condition and action has a name: an unnamed one is named from what it does,
    /// such as "MaximumRangeGRU is greater than 1000" or "Damage Multiply 2", as the HTML app's export does.
    /// </summary>
    private static JsonArray RulesWithRowNames(JsonArray rules)
    {
        var copy = (JsonArray)rules.DeepClone();
        foreach (JsonObject rule in copy.OfType<JsonObject>())
        {
            foreach (JsonObject condition in (rule["Conditions"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (JsonValues.IsTruthy(condition["Name"])) continue;
                string operand = JsonValues.AsText(JsonValues.IsTruthy(condition["ManualOverride"]) ? condition["CustomOperand"] : condition["Operand"]);
                condition["Name"] = operand + " " + JsonValues.AsText(condition["Operator"]) + " " + JsonValues.AsText(condition["Value"]);
            }
            foreach (JsonObject action in (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (JsonValues.IsTruthy(action["Name"])) continue;
                string target = JsonValues.AsText(JsonValues.IsTruthy(action["ManualOverride"]) ? action["CustomTarget"] : action["Target"]);
                action["Name"] = target + " " + WithoutTypeHint(JsonValues.AsText(action["Operator"])) + " " + JsonValues.AsText(action["Value"]);
            }
        }
        return copy;
    }

    /// <summary>"Multiply [int, float]" gives "Multiply": the part in brackets is a hint for the user, not part of the name.</summary>
    private static string WithoutTypeHint(string operatorName)
    {
        int bracket = operatorName.IndexOf('[', StringComparison.Ordinal);
        return bracket < 0 ? operatorName : operatorName[..bracket].TrimEnd(' ', '\t', '\r', '\n');
    }
}

/// <summary>A file that is not a mod or a backup. The message says why, in the HTML app's words.</summary>
public sealed class ModFileException(string message) : Exception(message);
