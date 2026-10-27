using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>Which units a generated division takes.</summary>
public enum GeneratedUnits
{
    /// <summary>Every unit of one nation (<see cref="DivisionGeneration.Nation"/>).</summary>
    Nation,

    /// <summary>Every unit of one side's nations (<see cref="DivisionGeneration.Side"/>).</summary>
    Side,

    /// <summary>Every unit of both sides, in a division of one side.</summary>
    Everything,
}

/// <summary>How many units a generated division gives each unit's card.</summary>
public enum GeneratedNumbers
{
    /// <summary>999 a card at every veterancy: no limit in practice.</summary>
    Unlimited,

    /// <summary>The same numbers for every unit, one for each veterancy (<see cref="DivisionGeneration.Counts"/>).</summary>
    Same,

    /// <summary>Each unit's own numbers, as the game's divisions give them (a similar unit's for a unit none has).</summary>
    AsTheGame,
}

/// <summary>
/// How a generated division is made: which units it takes and how many of each a card holds. The division keeps this,
/// not its units: they are made from the units of the files on the Files tab each time the mod is exported, so it
/// follows game updates (<see cref="DivisionRosters"/>). In the mod file it is the division's "Generation".
/// </summary>
public sealed record DivisionGeneration
{
    /// <summary>Which units it takes.</summary>
    public GeneratedUnits Units { get; init; } = GeneratedUnits.Nation;

    /// <summary>The nation whose units it takes, by its code ("SOV"), for <see cref="GeneratedUnits.Nation"/>.</summary>
    public string Nation { get; init; } = "";

    /// <summary>
    /// The side it is on ("NATO", "PACT"): the side whose nations' units it takes for <see cref="GeneratedUnits.Side"/>,
    /// and the side whose flag it is under for <see cref="GeneratedUnits.Everything"/>.
    /// </summary>
    public string Side { get; init; } = "";

    /// <summary>How many units each card holds.</summary>
    public GeneratedNumbers Numbers { get; init; } = GeneratedNumbers.Unlimited;

    /// <summary>
    /// For <see cref="GeneratedNumbers.Same"/>: the units a card holds at each veterancy, from the lowest, as YSM's
    /// Static divisions have them.
    /// </summary>
    public IReadOnlyList<int> Counts { get; init; } = [10, 6, 3, 1];

    /// <summary>How many cards of each category a deck may hold, each costing one point; also how many cards of each unit.</summary>
    public int Slots { get; init; } = 80;

    /// <summary>Whether each unit gets every transport the game accepts for it, among the division's own units.</summary>
    public bool AllTransports { get; init; } = true;

    /// <summary>
    /// Nations whose units go into every generated division, of either side, as well as its own: a mod's own nations,
    /// such as one of special units it adds for every army. By code ("YSM"); none at first.
    /// </summary>
    public IReadOnlyList<string> EveryDivisionNations { get; init; } = [];

    /// <summary>
    /// For <see cref="GeneratedNumbers.AsTheGame"/>: parts of the names of game divisions whose numbers are not learnt
    /// from, such as "_challenge_" for divisions made for single-player challenges. None at first.
    /// </summary>
    public IReadOnlyList<string> IgnoredDivisions { get; init; } = [];

    /// <summary>
    /// For <see cref="GeneratedNumbers.AsTheGame"/>: a game division's rule for a unit is not learnt from when it gives
    /// this many cards or units, or more (such rules are for single-player scenarios); null to learn from every rule.
    /// </summary>
    public int? IgnoredRulesFrom { get; init; }

    /// <summary>The units a card holds at most, at the lowest veterancy, for 999-a-card divisions.</summary>
    public const int UnlimitedCount = 999;

    /// <summary>
    /// The activation points a deck of it has: enough for every slot of every category, as YSM gives its own (its slots
    /// times nine categories).
    /// </summary>
    public int ActivationPoints => Slots * NewDivisionStart.WarnoCosts().Count;

    /// <summary>Its costs: every category with as many slots as <see cref="Slots"/>, each costing one point.</summary>
    public Dictionary<string, List<int>> Costs() =>
        NewDivisionStart.WarnoCosts().Keys.ToDictionary(category => category, _ => Enumerable.Repeat(1, Slots).ToList(), StringComparer.Ordinal);

    /// <summary>The generation as the mod file keeps it.</summary>
    public JsonObject ToJson() => new()
    {
        ["Units"] = Units.ToString(),
        ["Nation"] = Nation,
        ["Side"] = Side,
        ["Numbers"] = Numbers.ToString(),
        ["Counts"] = new JsonArray([.. Counts.Select(count => (JsonNode)JsonValue.Create(count))]),
        ["Slots"] = Slots,
        ["AllTransports"] = AllTransports,
        ["EveryDivisionNations"] = TextList(EveryDivisionNations),
        ["IgnoredDivisions"] = TextList(IgnoredDivisions),
        ["IgnoredRulesFrom"] = IgnoredRulesFrom,
    };

    private static JsonArray TextList(IEnumerable<string> texts) => new([.. texts.Select(text => (JsonNode)JsonValue.Create(text))]);

    private static List<string> TextsIn(JsonNode? list) =>
        list is JsonArray items ? [.. items.Select(JsonValues.AsText).Where(text => text != "")] : [];

    /// <summary>Reads a generation from the mod file; what it does not have is left as at first.</summary>
    public static DivisionGeneration FromJson(JsonObject json)
    {
        var made = new DivisionGeneration();
        return made with
        {
            Units = Enum.TryParse(JsonValues.AsText(json["Units"]), out GeneratedUnits units) ? units : made.Units,
            Nation = JsonValues.AsText(json["Nation"]),
            Side = JsonValues.AsText(json["Side"]),
            Numbers = Enum.TryParse(JsonValues.AsText(json["Numbers"]), out GeneratedNumbers numbers) ? numbers : made.Numbers,
            Counts = json["Counts"] is JsonArray counts && counts.Count == 4 ? [.. counts.Select(count => (int)(double.TryParse(JsonValues.AsText(count), out double each) ? each : 0))] : made.Counts,
            Slots = int.TryParse(JsonValues.AsText(json["Slots"]), out int slots) && slots > 0 ? slots : made.Slots,
            AllTransports = !JsonValues.IsFalse(json["AllTransports"]),
            EveryDivisionNations = TextsIn(json["EveryDivisionNations"]),
            IgnoredDivisions = TextsIn(json["IgnoredDivisions"]),
            IgnoredRulesFrom = int.TryParse(JsonValues.AsText(json["IgnoredRulesFrom"]), out int from) && from > 0 ? from : null,
        };
    }

    /// <summary>
    /// How it is made, in words, for the Armory's line above its units and its rule on the Rules tab: "every unit of the
    /// Soviet Union, 999 a card".
    /// </summary>
    public string Describe(Languages.TextCatalog texts)
    {
        string nation = NameIn(texts, NationTextPrefix + Nation, Nation);
        string side = NameIn(texts, SideTextPrefix + Side, Side);
        string units = Units switch
        {
            GeneratedUnits.Nation => texts.Get("Divisions.Generated.OfNation", ("nation", nation)),
            GeneratedUnits.Side => texts.Get("Divisions.Generated.OfSide", ("side", side)),
            _ => texts.Get("Divisions.Generated.OfEverything", ("side", side)),
        };
        string numbers = Numbers switch
        {
            GeneratedNumbers.Unlimited => texts.Get("Divisions.Generated.Unlimited.Short"),
            GeneratedNumbers.Same => texts.Get("Divisions.Generated.Same.Short", ("counts", string.Join(" · ", Counts))),
            _ => texts.Get("Divisions.Generated.AsTheGame.Short"),
        };
        string described = units + ", " + numbers;
        if (EveryDivisionNations.Count > 0)
            described += texts.Get("Divisions.Generated.WithNations", ("nations", string.Join(", ", EveryDivisionNations.Select(nation => NameIn(texts, NationTextPrefix + nation, nation)))));
        if (Numbers == GeneratedNumbers.AsTheGame && IgnoredDivisions.Count > 0)
            described += texts.Get("Divisions.Generated.IgnoringDivisions", ("names", string.Join(", ", IgnoredDivisions)));
        if (Numbers == GeneratedNumbers.AsTheGame && IgnoredRulesFrom is int from)
            described += texts.Get("Divisions.Generated.IgnoringRules", ("count", from));
        return described;
    }

    /// <summary>
    /// The start of the names of a nation's and a side's texts ("Units.Nation.SOV"), as the Armory names them. Written in
    /// two parts, so the text check does not take the start alone for a text's name.
    /// </summary>
    private const string NationTextPrefix = "Units" + ".Nation.", SideTextPrefix = "Units" + ".Side.";

    /// <summary>A name in the language in use; the code itself when the catalogue has no text for it.</summary>
    private static string NameIn(Languages.TextCatalog texts, string key, string code) => texts.English.ContainsKey(key) ? texts.Get(key) : code;

    /// <summary>Whether two generations make the same division.</summary>
    public bool SameAs(DivisionGeneration other) => ToJson().ToJsonString() == other.ToJson().ToJsonString();
}
