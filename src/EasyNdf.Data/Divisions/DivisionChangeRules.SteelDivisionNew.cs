using EasyNdf.Data.Game;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// A new Steel Division 2 division (see the format notes, section 4, and its new-division checklist): its descriptor in
/// Divisions.ndf, a copy of the division it starts from with GUIDs of its own; its entry in DivisionRules.ndf's MAP; a
/// cost matrix of its own in DivisionCostMatrix.ndf; its place in DivisionList.ndf, the list of the divisions the game
/// offers; its number for deck codes in DeckSerializer.ndf; its name and description, tokens whose texts go into the
/// mod's MODDING.csv; and its units, each with a pack of its own, as a unit put in a game division is.
/// </summary>
public sealed partial class DivisionChangeRules
{
    /// <summary>
    /// A new Steel Division 2 division's rules. It keeps the starting division's phases (their start times, bonuses and
    /// income), its income choices (DivisionEconomyList), portrait, type picture and power class, which the Armory does
    /// not change; the start is a game division of its nation, so they are the nation's. Everything else is its own:
    /// name, description, nation, side and tags, flag, activation points, costs, emblem and units.
    /// </summary>
    private void SteelDivisionNewDivision(ModDivision change, Division start, Division shown)
    {
        string division = shown.Descriptor;
        changeBeingWritten = "New";
        string costName = "MatrixCostName_" + CoreName(division);
        string summaryToken = TextToken(division, "summary");
        var actions = new JsonArray(
            Action("CountryId", "Set To [int, float, string, boolean]", Quoted(shown.Country)),
            Action(SteelDivisionDivisions.SideProperty, "Set To [int, float, string, boolean]", SteelDivisionDivisions.SidePrefix + shown.Side),
            // its starting division's tags with its own nation and side, its front among them (ModDivision.NewTags)
            Action("DivisionTags", "Set To [list, object]", TagsText(shown.Tags)),
            Action("MaxActivationPoints", "Set To [int, float, string, boolean]", Whole(shown.ActivationPoints ?? start.ActivationPoints)),
            Action("CostMatrix", "Set To [int, float, string, boolean]", costName),
            Action(SteelDivisionDivisions.SummaryProperty, "Set To [int, float, string, boolean]", "'" + summaryToken + "'"),
            Action("AvailableForPlay", "Set To [int, float, string, boolean]", "True"),
            // its cards are its own units' packs, added with them below
            Action("PackList", "Set To [list, object]", "MAP [ ]"));
        if (shown.Country != start.Country && start.FlagTextures.Count > 0)
            actions.Add(Action(SteelDivisionDivisions.FlagProperty, "Set To [list, object]",
                SteelDivisionDivisions.FlagText(SteelDivisionDivisions.FlagOf(shown.Country, start, gameDivisions))));
        actions.Add(Action("EmblemTexture", "Set To [int, float, string, boolean]", Quoted(shown.EmblemTexture)));
        JsonObject clone = Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewDivision"),
            ["Kind"] = "Clone",
            ["Params"] = new JsonObject { ["Source"] = start.Descriptor, ["NewName"] = division },
            ["Actions"] = actions,
        }, DivisionsFile, division, "Clone");
        // its description, for the export's MODDING.csv: never left to the game to find empty
        ((JsonObject)clone[ArmoryKey]!)[TextsKey] = new JsonObject { [summaryToken] = shown.Summary };
        NameRule(division, shown.Name, Line(shown, "Divisions.Rule.Name", ("name", shown.Name)));
        PhaseAndEconomyRules(change, start, shown);

        Rule(new JsonObject
        {
            ["Name"] = Line(shown, "Divisions.Rule.NewCosts"),
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = SteelDivisionCostMatrixText(costName, shown, start), ["Name"] = "" },
        }, CostsFile, division, "Add");
        Edit(DeckNumbersFile, DeckSerializerDescriptor, Line(shown, "Divisions.Rule.DeckNumber", ("number", change.DeckNumber)),
            [Action("DivisionIds", "Insert [array]", "LastIndex: (" + division + ", " + Whole(change.DeckNumber ?? 0) + ")")], division);
        JsonObject entry = Edit(RulesFile, SteelDivisionRulesDescriptor, Line(shown, "Divisions.Rule.NewUnitList"),
            [Action(SteelDivisionRulesDescriptor, "Insert [array]", "LastIndex: " + SteelDivisionEntryText(division, shown.Units, transport => TransportCapFor(transport, shown)))], division);
        Edit(SteelDivisionDivisions.ListFile, SteelDivisionDivisions.ListDescriptor, Line(shown, "Divisions.Rule.InList"),
            [Action(SteelDivisionDivisions.ListDescriptor, "Insert [array]", "LastIndex: ~/" + division)], division);

        if (change.Generation is DivisionGeneration generation)
        {
            GeneratedSteelDivisionRules(change, shown, generation, entry);
            return;
        }
        // each unit's pack, in the division's PackList, numbered for deck codes; its line is in the entry already
        foreach (DivisionUnit unit in shown.Units)
        {
            changeBeingWritten = "UnitIn:" + unit.Unit;
            SteelDivisionAddedUnitRules(division, shown, unit, writesLine: false);
        }
    }

    /// <summary>
    /// A generated division's units (Steel Division 2): what depends on them, written by the export each time from the
    /// files on the Files tab (<see cref="GeneratedUnitLists.Fill"/>), as WARNO's unit list is. They are in four places
    /// (see <see cref="SteelDivisionUnitRules"/>): its entry of DivisionRules.ndf, written whole with them; its PackList;
    /// a pack for each in Packs.ndf; and the packs' numbers for deck codes. Each such rule keeps the division's entry and
    /// its part; the entry's rule keeps the caps the game gives transports too, as the caps follow the units. Empty
    /// until the export fills them.
    /// </summary>
    private void GeneratedSteelDivisionRules(ModDivision change, Division shown, DivisionGeneration generation, JsonObject entry)
    {
        string division = shown.Descriptor;
        string how = Line(shown, "Divisions.Rule.GeneratedUnits", ("how", generation.Describe(texts)));
        entry["Name"] = how;
        MarkGenerated(entry, change, GeneratedParts.Entry);
        var caps = new JsonObject();
        foreach ((string transport, int cap) in gameDivisions.SelectMany(each => each.TransportCaps)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal).Select(group => (group.Key, group.Max(pair => pair.Value))).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            caps[transport] = cap;
        ((JsonObject)entry[ArmoryKey]!)[TransportCapsKey] = caps;
        MarkGenerated(Edit(DivisionsFile, division, how, Action("PackList", "Set To [list, object]", "MAP [ ]")), change, GeneratedParts.PackList);
        MarkGenerated(Rule(new JsonObject
        {
            ["Name"] = how,
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = "", ["Name"] = "" },
        }, PacksFile, division, "Add"), change, GeneratedParts.Packs);
        MarkGenerated(Edit(DeckNumbersFile, DeckSerializerDescriptor, how, Array.Empty<JsonObject>(), division), change, GeneratedParts.PackIds);
    }

    /// <summary>Marks a rule as one the export fills in for a generated division, with the division's entry and the part it writes.</summary>
    private static void MarkGenerated(JsonObject rule, ModDivision change, string part)
    {
        var armory = (JsonObject)rule[ArmoryKey]!;
        armory[GeneratedDivisionKey] = change.ToJson();
        armory[GeneratedPartKey] = part;
    }

    /// <summary>
    /// A division's entry of DivisionRules.ndf's MAP, whole: its units' lines, and a cap for each transport they ride in,
    /// as the game writes an entry, on one line.
    /// </summary>
    /// <param name="division">The division's descriptor.</param>
    /// <param name="units">Its units.</param>
    /// <param name="capFor">The cap a transport is given (<see cref="TransportCapFor"/>).</param>
    public static string SteelDivisionEntryText(string division, IEnumerable<DivisionUnit> units, Func<string, int> capFor)
    {
        List<DivisionUnit> all = [.. units];
        List<string> transports = [.. all.SelectMany(unit => unit.Transports).Distinct(StringComparer.Ordinal)];
        string lines = string.Join(", ", all.Select(SteelDivisionUnitLine));
        string caps = string.Join(", ", transports.Select(transport =>
            "TDeckTransportRule ( TransportDescriptor = ~/" + transport + " MaxNumber = " + Whole(capFor(transport)) + " )"));
        return "(~/" + division + ", TDeckDivisionRule ( UnitRuleList = [" + lines + "] TransportRuleList = [" + caps + "] ))";
    }

    /// <summary>
    /// A new division's cost matrix, as Steel Division 2 writes its own: "MatrixCostName_X is MAP [ (EDefaultFactories/tank,
    /// [1, 1, 2]), ]", each category under the key the starting division's matrix gives it (its lower-case alias), A to Z.
    /// A category the starting matrix has no key for is left out.
    /// </summary>
    private static string SteelDivisionCostMatrixText(string name, Division shown, Division start)
    {
        var lines = new List<string>();
        foreach ((string category, IReadOnlyList<int> costs) in shown.Costs)
        {
            if (SteelDivisionDivisions.CostKey(start, category) is not string key) continue;
            lines.Add("    (" + key.Replace(" ", "", StringComparison.Ordinal) + ", [" + Numbers(costs) + "]),\n");
        }
        lines.Sort(StringComparer.Ordinal);
        return name + " is MAP\n[\n" + string.Concat(lines) + "]";
    }

    /// <summary>
    /// A Steel Division 2 division's phases and income choices, when the change gives them: its PhaseList written whole,
    /// each phase as the game writes one (named PHASE_A, PHASE_B...), and its DivisionEconomyList. Nothing for WARNO,
    /// whose divisions have neither.
    /// </summary>
    private void PhaseAndEconomyRules(ModDivision change, Division before, Division shown)
    {
        if (game != GameTitle.SteelDivision2) return;
        string division = shown.Descriptor;
        if (change.Phases is not null && !change.Phases.SequenceEqual(before.Phases))
        {
            if (!change.IsNew) changeBeingWritten = "Phases";
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Phases", ("from", PhasesSummary(before.Phases)), ("to", PhasesSummary(change.Phases))),
                Action("PhaseList", "Set To [list, object]", PhaseListText(change.Phases)));
        }
        if (change.Economy is string economy && economy != before.Economy)
        {
            if (!change.IsNew) changeBeingWritten = "Economy";
            Edit(DivisionsFile, division, Line(shown, "Divisions.Rule.Economy", ("from", before.Economy), ("to", economy)),
                Action("DivisionEconomyList", "Set To [int, float, string, boolean]", economy));
        }
    }

    /// <summary>Phases as a rule's name gives them: "0 s 100, 600 s 140 (+20)", each its start, income and any bonus.</summary>
    private static string PhasesSummary(IEnumerable<DivisionPhase> phases) =>
        string.Join(", ", phases.Select(phase => Whole(phase.Start) + " s " + Whole(phase.Income) + (phase.Bonus != 0 ? " (+" + Whole(phase.Bonus) + ")" : "")));

    /// <summary>A PhaseList as Steel Division 2 writes one: a TPhaseDescriptor a phase, named PHASE_A, PHASE_B...</summary>
    public static string PhaseListText(IReadOnlyList<DivisionPhase> phases)
    {
        var text = new System.Text.StringBuilder("[\n");
        for (int phase = 0; phase < phases.Count; phase++)
            text.Append("    TPhaseDescriptor\n    (\n        PhaseName = 'PHASE_").Append((char)('A' + phase)).Append("'\n")
                .Append("        PhaseStartTimeInSeconds = ").Append(Whole(phases[phase].Start)).Append('\n')
                .Append("        PhaseBonus = ").Append(Whole(phases[phase].Bonus)).Append('\n')
                .Append("        PhaseIncome = ").Append(Whole(phases[phase].Income)).Append("\n    ),\n");
        return text.Append(']').ToString();
    }
}
