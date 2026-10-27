using System.Text.Json;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Mods;
using EasyNdf.Engine.Text;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Languages;

namespace EasyNdf.Data;

/// <summary>
/// The mod file format (<c>FormatVersion: 1</c>), shared with the HTML app: "Export mod", "Back up all mods", and reading
/// either kind back in.
/// </summary>
public static class ModFiles
{
    /// <summary>
    /// The text "Export mod" saves: <c>{ModName, FormatVersion, Watermark?, Presets: [{PresetName, Rules}], Divisions?, Units?, UnitEdits?, WeaponEdits?, Weapons?}</c>.
    /// The HTML app reads such a file but leaves out its Divisions, Units, UnitEdits, WeaponEdits and Weapons.
    /// Conditions and actions without a name get one made from what they do, as in the HTML app.
    /// </summary>
    public static string ExportText(Mod mod) => JsonFiles.ToText(ToJson(mod, nameRows: true));

    /// <summary>The text "Export mod" saves, saying too whether the mod is switched on, so an import keeps its switch.</summary>
    /// <param name="mod">The mod.</param>
    /// <param name="isOn">Whether it is switched on on the Mods tab.</param>
    /// <param name="store">Where the files of the mod's File rules are kept: the mod file carries them, so it can be shared whole. Null leaves them out.</param>
    public static string ExportText(Mod mod, bool isOn, ModFileStore? store = null)
    {
        JsonObject json = ToJson(mod, nameRows: true);
        json["On"] = isOn;
        if (store is not null && CarriedFilesToJson([mod], store) is JsonObject files) json["Files"] = files;
        return JsonFiles.ToText(json);
    }

    /// <summary>
    /// The files the mods' File rules carry, by the name they are kept under, each as base64 text; null when they
    /// carry none. A file the store no longer has is left out: the rule then says so on export.
    /// </summary>
    private static JsonObject? CarriedFilesToJson(IEnumerable<Mod> mods, ModFileStore store)
    {
        var files = new JsonObject();
        foreach (string name in mods.SelectMany(mod => FileAndTextRules.StoredNamesOf(mod.Presets.SelectMany(preset => preset.Rules))).Distinct(StringComparer.Ordinal))
            if (store.Read(name) is byte[] content) files[name] = Convert.ToBase64String(content);
        return files.Count > 0 ? files : null;
    }

    /// <summary>The files a mod file or backup carries, by name; none when it carries none, or one is not base64 text.</summary>
    private static Dictionary<string, byte[]> CarriedFilesFromJson(JsonNode? files)
    {
        var carried = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string name, JsonNode? text) in files as JsonObject ?? [])
        {
            try { carried[name] = Convert.FromBase64String(JsonValues.AsText(text)); }
            catch (FormatException) { /* a damaged file is left out; its rule says so on export */ }
        }
        return carried;
    }

    /// <summary>
    /// The mod as stored in the Mods folder: the same format as "Export mod", but rows without a name stay without one,
    /// so the Rules tab keeps showing them as they are.
    /// </summary>
    public static string StoredText(Mod mod) => JsonFiles.ToText(ToJson(mod, nameRows: false));

    /// <summary>
    /// The text "Back up all mods" saves: a list of every preset, each naming its mod. The first preset of each mod also
    /// carries the mod's watermark; versions before 0.71.0 ignore it. Mods without presets are not in it, as in the HTML app.
    /// </summary>
    /// <param name="mods">Every mod.</param>
    /// <param name="store">Where the files of the mods' File rules are kept: the first entry carries them all. Null leaves them out.</param>
    public static string BackupText(IEnumerable<Mod> mods, ModFileStore? store = null)
    {
        var all = new JsonArray();
        foreach (Mod mod in mods)
        {
            for (int i = 0; i < mod.Presets.Count; i++)
            {
                JsonObject entry = PresetToJson(mod.Presets[i], mod.Presets[i].Rules.DeepClone());
                entry["Mod"] = mod.Name;
                if (i == 0 && mod.Watermark is not null) entry["Watermark"] = mod.Watermark.ToJson();
                // the divisions ride with the first preset, as the watermark does
                if (i == 0 && mod.Divisions.Count > 0) entry["Divisions"] = DivisionsToJson(mod);
                if (i == 0 && mod.Units.Count > 0) entry["Units"] = UnitsToJson(mod);
                if (i == 0 && mod.UnitEdits.Count > 0) entry["UnitEdits"] = UnitEditsToJson(mod);
                if (i == 0 && mod.WeaponEdits.Count > 0) entry["WeaponEdits"] = WeaponEditsToJson(mod);
                if (i == 0 && mod.Weapons.Count > 0) entry["Weapons"] = WeaponsToJson(mod);
                all.Add(entry);
            }
        }
        if (store is not null && all.Count > 0 && CarriedFilesToJson(mods, store) is JsonObject files) all[0]!["Files"] = files;
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
        catch (JsonException error) { throw new ModFileException(DataTexts.Get("Rules.ModFile.NotJson", ("problem", error.Message))); }

        if (json is JsonObject modFile && modFile["Presets"] is JsonArray presets)
            return [ReadModFile(modFile, presets, NameFromFileName(fileName))];
        if (json is JsonArray list && IsBackup(list))
            return ReadBackup(list);
        if (LooksLikePresets(json))
            throw new ModFileException(DataTexts.Get("Rules.ModFile.IsPreset"));
        throw new ModFileException(DataTexts.Get("Rules.ModFile.NotAMod"));
    }

    private static Mod ReadModFile(JsonObject modFile, JsonArray presets, string fileBaseName)
    {
        string name = JsonValues.IsTruthy(modFile["ModName"]) ? JsText.Trim(JsonValues.AsText(modFile["ModName"])) : "";
        if (name == "") name = fileBaseName;
        Mod mod = ModWithPresets(name, presets);
        if (modFile["Watermark"] is JsonObject watermark) mod.Watermark = ModWatermark.FromJson(watermark);
        mod.Divisions = DivisionsFromJson(modFile["Divisions"]);
        mod.Units = UnitsFromJson(modFile["Units"]);
        mod.UnitEdits = UnitEditsFromJson(modFile["UnitEdits"]);
        mod.WeaponEdits = WeaponEditsFromJson(modFile["WeaponEdits"]);
        mod.Weapons = WeaponsFromJson(modFile["Weapons"]);
        mod.WasOn = modFile["On"] is JsonValue on && on.TryGetValue(out bool isOn) ? isOn : null;
        mod.CarriedFiles = CarriedFilesFromJson(modFile["Files"]);
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
            JsonObject? withDivisions = byMod[name].FirstOrDefault(e => e["Divisions"] is JsonArray);
            if (withDivisions is not null) mod.Divisions = DivisionsFromJson(withDivisions["Divisions"]);
            JsonObject? withUnits = byMod[name].FirstOrDefault(e => e["Units"] is JsonArray);
            if (withUnits is not null) mod.Units = UnitsFromJson(withUnits["Units"]);
            JsonObject? withUnitEdits = byMod[name].FirstOrDefault(e => e["UnitEdits"] is JsonArray);
            if (withUnitEdits is not null) mod.UnitEdits = UnitEditsFromJson(withUnitEdits["UnitEdits"]);
            JsonObject? withWeaponEdits = byMod[name].FirstOrDefault(e => e["WeaponEdits"] is JsonArray);
            if (withWeaponEdits is not null) mod.WeaponEdits = WeaponEditsFromJson(withWeaponEdits["WeaponEdits"]);
            JsonObject? withWeapons = byMod[name].FirstOrDefault(e => e["Weapons"] is JsonArray);
            if (withWeapons is not null) mod.Weapons = WeaponsFromJson(withWeapons["Weapons"]);
            // a backup's files ride with its first entry, for every mod
            mod.CarriedFiles = CarriedFilesFromJson(list.OfType<JsonObject>().FirstOrDefault()?["Files"]);
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
                throw new ModFileException(DataTexts.Get("Rules.ModFile.PresetWithoutRules", ("number", i + 1)));

        var mod = new Mod { Name = modName };
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = (JsonObject)presets[i]!;
            string presetName = JsText.Trim(JsonValues.AsText(preset["PresetName"]));
            if (presetName == "") presetName = modName + " " + (i + 1);
            mod.Presets.Add(new ModPreset
            {
                Name = mod.FreePresetName(presetName),
                Rules = RuleLoading.LoadRules(preset["Rules"]),
                ArmoryPart = ArmoryPartOf(preset),
                AppliesTo = AppliesToOf(preset),
            });
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
        json["Presets"] = new JsonArray(mod.Presets.Select(p => (JsonNode)PresetToJson(p, nameRows ? RulesWithRowNames(p.Rules) : p.Rules.DeepClone())).ToArray());
        if (mod.Divisions.Count > 0) json["Divisions"] = DivisionsToJson(mod);
        if (mod.Units.Count > 0) json["Units"] = UnitsToJson(mod);
        if (mod.UnitEdits.Count > 0) json["UnitEdits"] = UnitEditsToJson(mod);
        if (mod.WeaponEdits.Count > 0) json["WeaponEdits"] = WeaponEditsToJson(mod);
        if (mod.Weapons.Count > 0) json["Weapons"] = WeaponsToJson(mod);
        return json;
    }

    /// <summary>
    /// A preset as the mod file keeps it; an Armory preset says so (FromArmory) and which part's changes it keeps
    /// (ArmoryPart), and a rule set with files of its own lists them (AppliesTo). Other apps ignore both.
    /// </summary>
    private static JsonObject PresetToJson(ModPreset preset, JsonNode rules)
    {
        var json = new JsonObject { ["PresetName"] = preset.Name, ["Rules"] = rules };
        if (preset.ArmoryPart is ArmoryPart part)
        {
            json["FromArmory"] = true;
            json["ArmoryPart"] = part.ToString();
        }
        if (preset.AppliesTo is List<string> files) json["AppliesTo"] = new JsonArray([.. files.Select(file => (JsonNode?)file)]);
        return json;
    }

    /// <summary>The files a rule set in a mod file lists in "AppliesTo"; null when it has none, and applies to the files its name holds.</summary>
    public static List<string>? AppliesToOf(JsonObject preset)
    {
        if (preset["AppliesTo"] is not JsonArray files) return null;
        return [.. files.Select(file => JsText.Trim(JsonValues.AsText(file))).Where(file => file != "").Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The Armory part a preset in a mod file keeps the changes of; null for a preset of the user's own. Files from
    /// before the Armory had parts have one Armory preset, for Divisions.
    /// </summary>
    private static ArmoryPart? ArmoryPartOf(JsonObject preset)
    {
        if (!JsonValues.IsTruthy(preset["FromArmory"])) return null;
        return Enum.TryParse(JsonValues.AsText(preset["ArmoryPart"]), out ArmoryPart part) ? part : ArmoryPart.Divisions;
    }

    /// <summary>A mod's divisions as the mod file keeps them.</summary>
    private static JsonArray DivisionsToJson(Mod mod) => new([.. mod.Divisions.Select(division => (JsonNode)division.ToJson())]);

    /// <summary>The divisions a mod file keeps; none when it has none.</summary>
    private static List<Divisions.ModDivision> DivisionsFromJson(JsonNode? divisions) =>
        [.. (divisions as JsonArray ?? []).OfType<JsonObject>().Select(Divisions.ModDivision.FromJson)];

    /// <summary>The units a mod adds, as the mod file keeps them.</summary>
    private static JsonArray UnitsToJson(Mod mod) => new([.. mod.Units.Select(unit => (JsonNode)unit.ToJson())]);

    /// <summary>The units a mod file adds; none when it adds none.</summary>
    private static List<Units.ModUnit> UnitsFromJson(JsonNode? units) =>
        [.. (units as JsonArray ?? []).OfType<JsonObject>().Select(Units.ModUnit.FromJson)];

    /// <summary>A mod's changes to the game's units, as the mod file keeps them.</summary>
    private static JsonArray UnitEditsToJson(Mod mod) => new([.. mod.UnitEdits.Select(edit => (JsonNode)edit.ToJson())]);

    /// <summary>The changes to the game's units a mod file keeps; none when it has none.</summary>
    private static List<Units.UnitEdit> UnitEditsFromJson(JsonNode? edits) =>
        [.. (edits as JsonArray ?? []).OfType<JsonObject>().Select(Units.UnitEdit.FromJson)];

    /// <summary>A mod's changes to the game's weapons, as the mod file keeps them.</summary>
    private static JsonArray WeaponEditsToJson(Mod mod) => new([.. mod.WeaponEdits.Select(edit => (JsonNode)edit.ToJson())]);

    /// <summary>The changes to the game's weapons a mod file keeps; none when it has none.</summary>
    private static List<Weapons.WeaponEdit> WeaponEditsFromJson(JsonNode? edits) =>
        [.. (edits as JsonArray ?? []).OfType<JsonObject>().Select(Weapons.WeaponEdit.FromJson)];

    /// <summary>The weapons a mod adds, as the mod file keeps them.</summary>
    private static JsonArray WeaponsToJson(Mod mod) => new([.. mod.Weapons.Select(weapon => (JsonNode)weapon.ToJson())]);

    /// <summary>The weapons a mod file adds; none when it adds none.</summary>
    private static List<Weapons.ModWeapon> WeaponsFromJson(JsonNode? weapons) =>
        [.. (weapons as JsonArray ?? []).OfType<JsonObject>().Select(Weapons.ModWeapon.FromJson)];

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
