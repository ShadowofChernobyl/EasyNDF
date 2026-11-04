using System.Globalization;
using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The unit lists of generated divisions, made at export: the rule that sets a generated division's list
/// (<see cref="DivisionChangeRules.GeneratedDivisionKey"/>) is given the list its generation makes from the units of the
/// files on the Files tab, with the user's own changes on top.
/// </summary>
public static class GeneratedUnitLists
{
    /// <summary>The value such a rule has until the export makes its list: an empty list.</summary>
    public const string NotYetMade = "[]";

    /// <summary>Whether any of the rules sets a generated division's unit list.</summary>
    public static bool AnyIn(IEnumerable<JsonNode?> rules) => rules.Any(IsGeneratedListRule);

    /// <summary>Whether a rule sets a generated division's unit list.</summary>
    public static bool IsGeneratedListRule(JsonNode? rule) =>
        rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.GeneratedDivisionKey] is JsonObject;

    /// <summary>The generated divisions' entries the rules make lists for, for the checksum of what the lists are made from.</summary>
    public static IEnumerable<JsonObject> EntriesIn(IEnumerable<JsonNode?> rules) =>
        rules.Where(IsGeneratedListRule).Select(rule => (JsonObject)rule![DivisionChangeRules.ArmoryKey]![DivisionChangeRules.GeneratedDivisionKey]!);

    /// <summary>
    /// Gives each rule that sets a generated division's unit list the list its generation makes, the user's changes on
    /// top. Returns the lists made, by division descriptor, so an export can keep them for the next one.
    /// </summary>
    /// <param name="rules">The rules to fill, changed in place.</param>
    /// <param name="units">The units of each generated division, by its descriptor (<see cref="UnitsOf"/>).</param>
    public static void Fill(IEnumerable<JsonNode?> rules, IReadOnlyDictionary<string, IReadOnlyList<DivisionUnit>> units)
    {
        // a rule made editable (unlocked) is the user's own from then on: its list is not made again over their edits
        // the packs' numbers for deck codes, so no two of this export's share one
        var packNumbers = new HashSet<int>();
        foreach (JsonNode? rule in rules.Where(rule => IsGeneratedListRule(rule) && ArmoryPresets.IsLocked(rule)))
        {
            JsonObject armory = (JsonObject)rule![DivisionChangeRules.ArmoryKey]!;
            string division = JsonValues.AsText(armory[DivisionChangeRules.GeneratedDivisionKey]!["Descriptor"]);
            if (!units.TryGetValue(division, out IReadOnlyList<DivisionUnit>? list)) continue;
            if (armory[DivisionChangeRules.GeneratedPartKey] is JsonNode part)
            {
                FillSteelDivisionPart((JsonObject)rule, JsonValues.AsText(part), division, list, packNumbers);
                continue;
            }
            foreach (JsonObject action in (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
                if (JsonValues.AsText(action["Target"]) == "UnitRuleList") action["Value"] = ListText(list);
        }
    }

    /// <summary>
    /// Fills one part of a Steel Division 2 generated division with its units (<see cref="DivisionChangeRules.GeneratedParts"/>):
    /// its entry of DivisionRules.ndf, with a cap for each transport (the highest the game gives it, else 1); its
    /// PackList, a card count for each unit's pack; a pack for each unit; and each pack's number for deck codes.
    /// </summary>
    private static void FillSteelDivisionPart(JsonObject rule, string part, string division, IReadOnlyList<DivisionUnit> list, HashSet<int> packNumbers)
    {
        string PackOf(DivisionUnit unit) => DivisionChangeRules.OwnPackOf(division, unit.Unit);
        switch (part)
        {
            case DivisionChangeRules.GeneratedParts.Entry:
                JsonObject caps = rule[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.TransportCapsKey] as JsonObject ?? [];
                int CapFor(string transport) => caps[transport] is JsonNode cap && int.TryParse(JsonValues.AsText(cap), out int highest) && highest > 0 ? highest : 1;
                foreach (JsonObject action in (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
                    action["Value"] = "LastIndex: " + DivisionChangeRules.SteelDivisionEntryText(division, list, CapFor);
                break;
            case DivisionChangeRules.GeneratedParts.PackList:
                foreach (JsonObject action in (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
                    action["Value"] = "MAP [" + string.Join(", ", list.Select(unit => "(~/" + PackOf(unit) + ", " + unit.Cards.ToString(CultureInfo.InvariantCulture) + ")")) + "]";
                break;
            case DivisionChangeRules.GeneratedParts.Packs:
                if (rule["Params"] is JsonObject parameters)
                    parameters["Text"] = string.Join("\n", list.Select(unit => DivisionChangeRules.PackText(PackOf(unit), unit.Unit)));
                break;
            case DivisionChangeRules.GeneratedParts.PackIds:
                rule["Actions"] = new JsonArray([.. list.Select(unit => (JsonNode)DivisionChangeRules.Action("PackIds", "Insert [array]",
                    "LastIndex: (" + PackOf(unit) + ", " + DivisionChangeRules.PackNumberFor(PackOf(unit), packNumbers).ToString(CultureInfo.InvariantCulture) + ")"))]);
                break;
        }
    }

    /// <summary>
    /// The units each generated division the rules are for has: those its generation gives (<see cref="DivisionRosters"/>),
    /// less those the user took out, with the user's changes to them and the units the user added.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<DivisionUnit>> UnitsOf(IEnumerable<JsonNode?> rules, DivisionRosters rosters)
    {
        var lists = new Dictionary<string, IReadOnlyList<DivisionUnit>>(StringComparer.Ordinal);
        foreach (JsonObject json in EntriesIn(rules))
        {
            ModDivision entry = ModDivision.FromJson(json);
            // the frame its file entries are copied from plays no part in its units
            var frame = new Division { Descriptor = entry.StartsFrom, Name = entry.Name };
            lists[entry.Descriptor] = entry.Apply(rosters.StartOf(entry, frame)).Units;
        }
        return lists;
    }

    /// <summary>A unit list as the game writes it: one unit a line.</summary>
    public static string ListText(IEnumerable<DivisionUnit> units)
    {
        List<string> lines = [.. units.Select(DivisionChangeRules.UnitLine)];
        return lines.Count == 0 ? NotYetMade : "[\n    " + string.Join(",\n    ", lines) + ",\n]";
    }
}
