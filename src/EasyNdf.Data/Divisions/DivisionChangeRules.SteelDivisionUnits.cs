using System.Globalization;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// Steel Division 2's units in a division (see the format notes, section 4). The game keeps them in four places that
/// change together: the division's entry of the one MAP in DivisionRules.ndf (its UnitRuleList, and its transports' caps
/// in TransportRuleList), the division's PackList in Divisions.ndf (a pack for each unit, with as many cards as the
/// unit's MaxPackNumber), the pack itself in Packs.ndf, and the pack's number in DeckSerializer.ndf's PackIds. A rule
/// reaches one division's entry of the MAP with a path part in square brackets, "DivisionRules &gt; [~/Division]".
/// </summary>
public sealed partial class DivisionChangeRules
{
    /// <summary>Steel Division 2's file of packs, the bundles of one unit a division's cards are.</summary>
    public const string PacksFile = "Packs.ndf";

    /// <summary>The descriptor of Steel Division 2's DivisionRules.ndf, which holds every division's units in one MAP.</summary>
    private const string SteelDivisionRulesDescriptor = "DivisionRules";

    /// <summary>The descriptor of DeckSerializer.ndf, which numbers divisions, packs and units for deck codes.</summary>
    private const string DeckSerializerDescriptor = "DeckSerializer";

    /// <summary>
    /// The numbers for deck codes a pack the Armory adds is given: above the game's highest (357,560 in its files), and
    /// below 524,288, so they take no more room in a deck code than the game's own <b>(inferred)</b>.
    /// </summary>
    private const int FirstPackNumber = 360000, PackNumberCount = 164288;

    /// <summary>The packs' numbers given by this writer, so two packs never share one.</summary>
    private readonly HashSet<int> packNumbersGiven = [];

    /// <summary>
    /// A Steel Division 2 division's units changed: each unit taken out, put in, or changed, in all four places the
    /// game keeps it (see the class's summary). <paramref name="before"/> is the division as the game has it.
    /// </summary>
    private void SteelDivisionUnitRules(Division before, Division shown)
    {
        if (!Writes(game, DivisionDetail.Units)) return;
        string division = shown.Descriptor;
        var capped = new HashSet<string>(before.TransportCaps.Keys, StringComparer.Ordinal);
        foreach (DivisionUnit gone in before.Units.Where(unit => !shown.Units.Any(each => each.Unit == unit.Unit)))
        {
            changeBeingWritten = "UnitOut:" + gone.Unit;
            string title = Line(shown, gone.Unit, "Divisions.Rule.UnitTakenOut");
            Edit(RulesFile, SteelDivisionRulesDescriptor, title,
                [Action(UnitRulePath(division, gone.Unit), ActionApplier.RemovePropertyOperator, "")], division);
            Edit(DivisionsFile, division, title, [Action("PackList > [~/" + GamePackOf(division, gone.Unit) + "]", ActionApplier.RemovePropertyOperator, "")], division);
        }
        foreach (DivisionUnit unit in shown.Units)
        {
            DivisionUnit? was = before.Units.FirstOrDefault(each => each.Unit == unit.Unit);
            // a pack added to a DLC division stops the game (SteelDivisionDivisions.IsInDlc): a mod made before that was known keeps the unit, unwritten
            if (was is null && SteelDivisionDivisions.IsInDlc(division)) continue;
            changeBeingWritten = (was is null ? "UnitIn:" : "Unit:") + unit.Unit;
            if (was is null) SteelDivisionAddedUnitRules(division, shown, unit);
            else SteelDivisionChangedUnitRules(division, shown, was, unit);
            TransportCapRules(division, shown, unit, capped);
        }
    }

    /// <summary>
    /// A unit put in a division: its line at the end of the division's UnitRuleList, a pack of its own in Packs.ndf
    /// (named after the division and the unit, as the game names its own, with "EZ" so it is never one of the game's),
    /// the pack in the division's PackList with the unit's cards, and the pack's number for deck codes.
    /// </summary>
    /// <param name="division">The division's descriptor.</param>
    /// <param name="shown">The division as the Armory shows it, for the rules' names.</param>
    /// <param name="unit">The unit, as the division has it.</param>
    /// <param name="writesLine">
    /// Whether its line is put in the division's entry of DivisionRules.ndf: a new division's entry is written whole,
    /// its units' lines with it (<see cref="SteelDivisionNewDivision"/>), as a rule cannot reach inside an entry another
    /// rule of the same export has just put in.
    /// </param>
    private void SteelDivisionAddedUnitRules(string division, Division shown, DivisionUnit unit, bool writesLine = true)
    {
        string title = Line(shown, unit.Unit, "Divisions.Rule.UnitAdded", ("cards", unit.Cards), ("units", PhasesText(PhasesOf(unit))));
        string pack = OwnPackOf(division, unit.Unit);
        if (writesLine)
            Edit(RulesFile, SteelDivisionRulesDescriptor, title,
                [Action(DivisionEntryPath(division) + ActionApplier.PathSeparator + "UnitRuleList", "Insert [array]", "LastIndex: " + SteelDivisionUnitLine(unit))], division);
        Rule(new JsonObject
        {
            ["Name"] = title,
            ["Kind"] = "Add",
            ["Params"] = new JsonObject { ["Text"] = PackText(pack, unit.Unit), ["Name"] = "" },
        }, PacksFile, division, "Add");
        Edit(DivisionsFile, division, title, [Action("PackList", "Insert [array]", "LastIndex: (~/" + pack + ", " + Whole(unit.Cards) + ")")], division);
        Edit(DeckNumbersFile, DeckSerializerDescriptor, title,
            [Action("PackIds", "Insert [array]", "LastIndex: (" + pack + ", " + PackNumberFor(pack).ToString(CultureInfo.InvariantCulture) + ")")], division);
    }

    /// <summary>
    /// A unit the division has already, changed: a rule for each of its values that changed, on the unit's line in the
    /// division's entry; its cards in the division's PackList too.
    /// </summary>
    private void SteelDivisionChangedUnitRules(string division, Division shown, DivisionUnit was, DivisionUnit unit)
    {
        string path = UnitRulePath(division, unit.Unit) + ActionApplier.PathSeparator;
        void Set(string textName, object? from, object? to, string file, string descriptor, params JsonObject[] actions)
        {
            // the value's own change, named as its text is: "Unit:Descriptor_Unit_X:Cards"
            changeBeingWritten = "Unit:" + unit.Unit + ":" + textName.Split('.')[^1];
            Edit(file, descriptor, Line(shown, unit.Unit, textName, ("from", from), ("to", to)), actions, division);
        }
        if (unit.Cards != was.Cards)
        {
            Set("Divisions.Rule.Cards", was.Cards, unit.Cards, RulesFile, SteelDivisionRulesDescriptor,
                Action(path + "MaxPackNumber", "Set To [int, float, string, boolean]", Whole(unit.Cards)));
            // the pack's cards are the unit's in every one of the game's divisions
            Set("Divisions.Rule.Cards", was.Cards, unit.Cards, DivisionsFile, division,
                Action("PackList" + ActionApplier.PathSeparator + "~/" + GamePackOf(division, unit.Unit), "Set To [int, float, string, boolean]", Whole(unit.Cards)));
        }
        if (!PhasesOf(unit).SequenceEqual(PhasesOf(was)))
            Set("Divisions.Rule.UnitsPerCard", PhasesText(PhasesOf(was)), PhasesText(PhasesOf(unit)), RulesFile, SteelDivisionRulesDescriptor,
                Action(path + "NumberOfUnitInPackByPhase", "Set To [list, object]", "[" + Numbers(PhasesOf(unit)) + "]"));
        if (!VeterancyOf(unit).SequenceEqual(VeterancyOf(was)))
            Set("Divisions.Rule.Veterancy", Decimals(VeterancyOf(was)), Decimals(VeterancyOf(unit)), RulesFile, SteelDivisionRulesDescriptor,
                Action(path + "NumberOfUnitInPackXPMultiplier", "Set To [list, object]", "[" + Decimals(VeterancyOf(unit)) + "]"));
        if (unit.AvailableWithoutTransport != was.AvailableWithoutTransport)
            Set("Divisions.Rule.NoTransport", YesOrNo(was.AvailableWithoutTransport), YesOrNo(unit.AvailableWithoutTransport), RulesFile, SteelDivisionRulesDescriptor,
                Action(path + "AvailableWithoutTransport", "Set To [int, float, string, boolean]", unit.AvailableWithoutTransport ? "True" : "False"));
        if (!unit.Transports.SequenceEqual(was.Transports))
        {
            // the list is set where the unit has one, and added where it has none (a unit that needed no transport)
            string list = SteelDivisionUnitList(unit.Transports);
            Set("Divisions.Rule.Transports", Names(was.Transports), Names(unit.Transports), RulesFile, SteelDivisionRulesDescriptor,
                Action(path + "AvailableTransportList", "Set To [list, object]", list),
                Action(UnitRulePath(division, unit.Unit) + ActionApplier.PathSeparator + "AvailableTransportList", "Add Property", list));
        }
    }

    /// <summary>
    /// A cap for each transport a unit rides in that the division does not cap yet: every transport the game's divisions
    /// use has one in their TransportRuleList. It is the highest the game gives that transport in any division, else
    /// the highest the division gives any.
    /// </summary>
    /// <param name="capped">The transports the division caps, those the rules add among them.</param>
    private void TransportCapRules(string division, Division shown, DivisionUnit unit, HashSet<string> capped)
    {
        foreach (string transport in unit.Transports.Where(capped.Add))
        {
            int highest = TransportCapFor(transport, shown);
            changeBeingWritten = "Unit:" + unit.Unit + ":TransportCap:" + transport;
            Edit(RulesFile, SteelDivisionRulesDescriptor, Line(shown, transport, "Divisions.Rule.TransportCap", ("count", highest)),
                [Action(DivisionEntryPath(division) + ActionApplier.PathSeparator + "TransportRuleList", "Insert [array]",
                    "LastIndex: TDeckTransportRule ( TransportDescriptor = ~/" + transport + " MaxNumber = " + Whole(highest) + " )")], division);
        }
    }

    /// <summary>
    /// The cap a transport the division does not cap yet is given: the highest the game gives it in any division, else
    /// the highest the division gives any transport, else 1.
    /// </summary>
    private int TransportCapFor(string transport, Division shown)
    {
        int highest = gameDivisions.Select(each => each.TransportCaps.GetValueOrDefault(transport)).DefaultIfEmpty(0).Max();
        if (highest <= 0) highest = shown.TransportCaps.Values.DefaultIfEmpty(0).Max();
        return highest > 0 ? highest : 1;
    }

    // ---- names and lines ----

    /// <summary>The path to a division's entry of DivisionRules.ndf's MAP: "&gt; DivisionRules &gt; [~/Descriptor_Deck_Division_X]".</summary>
    private static string DivisionEntryPath(string division) =>
        "> " + SteelDivisionRulesDescriptor + ActionApplier.PathSeparator + "[~/" + division + "]";

    /// <summary>The path to one unit's line in a division's entry: "… &gt; UnitRuleList &gt; [~/Descriptor_Unit_X]".</summary>
    private static string UnitRulePath(string division, string unit) =>
        DivisionEntryPath(division) + ActionApplier.PathSeparator + "UnitRuleList" + ActionApplier.PathSeparator + "[~/" + unit + "]";

    /// <summary>
    /// The game's pack of a unit in a division, as the game names every one: "Descriptor_Deck_Pack_TOE_" and the
    /// division's and the unit's names without their prefixes. Every unit of the game's divisions has its pack so named,
    /// its defences too: "Descriptor_Production_Barbed_Wire" is in "Descriptor_Deck_Pack_TOE_&lt;division&gt;_Barbed_Wire".
    /// </summary>
    public static string GamePackOf(string division, string unit) => "Descriptor_Deck_Pack_TOE_" + CoreOf(division, "Descriptor_Deck_Division_") + "_" + UnitCoreOf(unit);

    /// <summary>The pack of a unit the mod puts in a division: "Descriptor_Deck_Pack_EZ_" and the division's and the unit's names.</summary>
    public static string OwnPackOf(string division, string unit) => "Descriptor_Deck_Pack_EZ_" + CoreOf(division, "Descriptor_Deck_Division_") + "_" + UnitCoreOf(unit);

    private static string CoreOf(string name, string prefix) => name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;

    /// <summary>A unit's name without "Descriptor_Unit_", or a defence's without "Descriptor_Production_", as a pack's name holds it.</summary>
    private static string UnitCoreOf(string unit) => CoreOf(CoreOf(unit, "Descriptor_Unit_"), "Descriptor_Production_");

    /// <summary>
    /// A pack's number for deck codes: one made from its name, so it stays the same from one export to the next, the
    /// next free one when another pack has it.
    /// </summary>
    private int PackNumberFor(string pack)
    {
        string hex = StableGuid.From(pack).Replace("GUID:{", "", StringComparison.Ordinal)[..8];
        int number = FirstPackNumber + (int)(uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) % PackNumberCount);
        while (!packNumbersGiven.Add(number)) number = number + 1 < FirstPackNumber + PackNumberCount ? number + 1 : FirstPackNumber;
        return number;
    }

    /// <summary>A pack of Packs.ndf holding one unit, written as the game writes its own, with a GUID of its own.</summary>
    public static string PackText(string pack, string unit) =>
        "export " + pack + " is TDeckPackDescriptor\n(\n    DescriptorId = " + StableGuid.From(pack) + "\n    TransporterAndUnitsList =\n    [\n"
        + "        TDeckTransporterAndUnitsDescriptor\n        (\n            UnitDescriptorList =\n            [\n                ~/" + unit + ",\n            ]\n        ),\n    ]\n)";

    /// <summary>A unit's whole line in a division's UnitRuleList, as Steel Division 2 writes one, on one line.</summary>
    public static string SteelDivisionUnitLine(DivisionUnit unit)
    {
        var parts = new List<string>
        {
            "UnitDescriptor = ~/" + unit.Unit,
            "AvailableWithoutTransport = " + (unit.AvailableWithoutTransport ? "True" : "False"),
        };
        if (unit.Transports.Count > 0) parts.Add("AvailableTransportList = " + SteelDivisionUnitList(unit.Transports));
        parts.Add("MaxPackNumber = " + Whole(unit.Cards));
        parts.Add("NumberOfUnitInPackByPhase = [" + Numbers(PhasesOf(unit)) + "]");
        parts.Add("NumberOfUnitInPackXPMultiplier = [" + Decimals(VeterancyOf(unit)) + "]");
        return "TDeckUniteRule ( " + string.Join(" ", parts) + " )";
    }

    private static string SteelDivisionUnitList(IEnumerable<string> units) => "[" + string.Join(", ", units.Select(unit => "~/" + unit)) + "]";

    /// <summary>
    /// A unit's units per card in each of the three phases: its own; for one given a single number (as WARNO keeps it),
    /// that number in each phase.
    /// </summary>
    private static IReadOnlyList<int> PhasesOf(DivisionUnit unit) =>
        unit.UnitsPerCardByPhase.Count == 3 ? unit.UnitsPerCardByPhase : [unit.UnitsPerCard ?? 1, unit.UnitsPerCard ?? 1, unit.UnitsPerCard ?? 1];

    /// <summary>
    /// A unit's three veterancy shares, as Steel Division 2 has three levels: the first three given, 0 for the rest,
    /// the lowest level alone when none is offered.
    /// </summary>
    private static IReadOnlyList<double> VeterancyOf(DivisionUnit unit)
    {
        List<double> levels = [.. unit.VeterancyMultipliers.Take(3)];
        while (levels.Count < 3) levels.Add(0);
        if (levels.All(level => level == 0)) levels[0] = 1;
        return levels;
    }

    private static string PhasesText(IEnumerable<int> phases) => string.Join(" / ", phases.Select(phase => Whole(phase)));
}
