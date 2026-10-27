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
        foreach (JsonNode? rule in rules.Where(IsGeneratedListRule))
        {
            string division = JsonValues.AsText(rule![DivisionChangeRules.ArmoryKey]![DivisionChangeRules.GeneratedDivisionKey]!["Descriptor"]);
            if (!units.TryGetValue(division, out IReadOnlyList<DivisionUnit>? list)) continue;
            foreach (JsonObject action in (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
                if (JsonValues.AsText(action["Target"]) == "UnitRuleList") action["Value"] = ListText(list);
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
