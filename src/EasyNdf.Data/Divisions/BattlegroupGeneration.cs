using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>What a battlegroup's generator weighs units by: the eight sliders of the Generation Settings window, in their order.</summary>
public enum BattlegroupMeasure
{
    /// <summary>How hard its weapons hit armour, and from how far.</summary>
    AgainstArmour,

    /// <summary>How much damage and suppression its weapons deal to infantry and light targets.</summary>
    AgainstInfantry,

    /// <summary>How well its anti-air weapons reach and hit helicopters and planes.</summary>
    AgainstAircraft,

    /// <summary>How much it takes to destroy: its strength and front armour.</summary>
    Survivability,

    /// <summary>How fast it moves.</summary>
    Mobility,

    /// <summary>How well it sees: its optics and vision.</summary>
    ReconAndOptics,

    /// <summary>How much of all the above it gives for its cost in points.</summary>
    ValueForPoints,

    /// <summary>How strongly the battlegroup takes units of different roles before more of one role.</summary>
    Variety,
}

/// <summary>
/// How a generated division's Official Battlegroup is made (<see cref="BattlegroupGenerator"/>): the ready setting it
/// started from, the weight of each measure, which picks each role gets, and its size. It is kept with the division in
/// the mod ("BattlegroupGeneration"); the cards are made again at each export from the division's units.
/// </summary>
public sealed record BattlegroupGeneration
{
    /// <summary>The name of the setting the weights match, such as "Balanced"; <see cref="Custom"/> once one is changed.</summary>
    public string ReadySetting { get; init; } = "Balanced";

    /// <summary>The weight of each measure, 0 to 100, in <see cref="BattlegroupMeasure"/>'s order.</summary>
    public IReadOnlyList<int> Weights { get; init; } = ReadySettings["Balanced"];

    /// <summary>Whether each role gets its strongest unit.</summary>
    public bool TakesBest { get; init; } = true;

    /// <summary>Whether each role gets the unit that gives the most for its cost.</summary>
    public bool TakesGoodValue { get; init; } = true;

    /// <summary>Whether each role gets a cheap unit that still does the job.</summary>
    public bool TakesCheap { get; init; } = true;

    /// <summary>The cards each category holds at most (no more than the division has card slots for).</summary>
    public int CardsPerCategory { get; init; } = 6;

    /// <summary>The activation points the battlegroup costs at most; null for the division's own.</summary>
    public int? PointsAtMost { get; init; }

    /// <summary>The name a setting has once its weights are changed by hand.</summary>
    public const string Custom = "Custom";

    /// <summary>
    /// The ready settings, by name, in the order the window offers them: the weight of each measure, in
    /// <see cref="BattlegroupMeasure"/>'s order.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> ReadySettings { get; } = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
    {
        // armour, infantry, aircraft, survivability, mobility, recon, value, variety
        ["Balanced"] = [60, 60, 50, 50, 30, 30, 50, 60],
        ["Strongest"] = [80, 80, 70, 80, 30, 30, 0, 40],
        ["Cheapest"] = [40, 40, 40, 20, 20, 20, 100, 50],
        ["AntiTank"] = [100, 30, 30, 50, 30, 20, 40, 40],
        ["AirDefence"] = [30, 30, 100, 40, 30, 20, 40, 40],
    };

    /// <summary>The names of a ready setting's text and tooltip (Custom's for a name not known).</summary>
    public static (string Name, string Tip) TextNamesOf(string readySetting) => readySetting switch
    {
        "Balanced" => ("Divisions.Battlegroup.Generation.Ready.Balanced", "Divisions.Battlegroup.Generation.Ready.Balanced.Tip"),
        "Strongest" => ("Divisions.Battlegroup.Generation.Ready.Strongest", "Divisions.Battlegroup.Generation.Ready.Strongest.Tip"),
        "Cheapest" => ("Divisions.Battlegroup.Generation.Ready.Cheapest", "Divisions.Battlegroup.Generation.Ready.Cheapest.Tip"),
        "AntiTank" => ("Divisions.Battlegroup.Generation.Ready.AntiTank", "Divisions.Battlegroup.Generation.Ready.AntiTank.Tip"),
        "AirDefence" => ("Divisions.Battlegroup.Generation.Ready.AirDefence", "Divisions.Battlegroup.Generation.Ready.AirDefence.Tip"),
        _ => ("Divisions.Battlegroup.Generation.Ready.Custom", "Divisions.Battlegroup.Generation.Ready.Custom.Tip"),
    };

    /// <summary>The ready setting a set of weights matches; <see cref="Custom"/> when none does.</summary>
    public static string ReadySettingOf(IReadOnlyList<int> weights) =>
        ReadySettings.FirstOrDefault(ready => ready.Value.SequenceEqual(weights)).Key ?? Custom;

    /// <summary>A measure's weight.</summary>
    public int WeightOf(BattlegroupMeasure measure) => (int)measure < Weights.Count ? Weights[(int)measure] : 0;

    /// <summary>The settings as the mod file keeps them.</summary>
    public JsonObject ToJson() => new()
    {
        ["ReadySetting"] = ReadySetting,
        ["Weights"] = new JsonArray([.. Weights.Select(weight => (JsonNode)JsonValue.Create(weight))]),
        ["TakesBest"] = TakesBest,
        ["TakesGoodValue"] = TakesGoodValue,
        ["TakesCheap"] = TakesCheap,
        ["CardsPerCategory"] = CardsPerCategory,
        ["PointsAtMost"] = PointsAtMost,
    };

    /// <summary>Reads the settings from the mod file; what it does not have is left as at first.</summary>
    public static BattlegroupGeneration FromJson(JsonObject json)
    {
        var made = new BattlegroupGeneration();
        int measures = Enum.GetValues<BattlegroupMeasure>().Length;
        List<int>? weights = json["Weights"] is JsonArray list && list.Count == measures
            ? [.. list.Select(item => Math.Clamp(int.TryParse(JsonValues.AsText(item), out int weight) ? weight : 0, 0, 100))]
            : null;
        return made with
        {
            ReadySetting = JsonValues.AsText(json["ReadySetting"]) is { Length: > 0 } ready ? ready : made.ReadySetting,
            Weights = weights ?? made.Weights,
            TakesBest = !JsonValues.IsFalse(json["TakesBest"]),
            TakesGoodValue = !JsonValues.IsFalse(json["TakesGoodValue"]),
            TakesCheap = !JsonValues.IsFalse(json["TakesCheap"]),
            CardsPerCategory = int.TryParse(JsonValues.AsText(json["CardsPerCategory"]), out int cards) && cards > 0 ? cards : made.CardsPerCategory,
            PointsAtMost = int.TryParse(JsonValues.AsText(json["PointsAtMost"]), out int points) && points > 0 ? points : null,
        };
    }

    /// <summary>Whether two settings make the same battlegroup.</summary>
    public bool SameAs(BattlegroupGeneration other) => ToJson().ToJsonString() == other.ToJson().ToJsonString();
}
