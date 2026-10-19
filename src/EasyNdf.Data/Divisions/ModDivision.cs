using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// A division as a mod keeps it: either a change to one of the game's divisions (its details, its units, or taken out
/// of the game's list), or a new division that starts as a copy of a game division's details (points, costs, emblem,
/// type). Only the changes are
/// kept, not the whole division, so they apply again to the game's files after a game update. In the mod file it is
/// one entry of <c>Divisions</c>.
/// </summary>
public sealed class ModDivision
{
    /// <summary>
    /// The division's descriptor: the game division's own for a change; a new one for a new division
    /// ("Descriptor_Deck_Division_My_Division_multi").
    /// </summary>
    public string Descriptor { get; set; } = "";

    /// <summary>Whether it is a new division, rather than a change to a game division.</summary>
    public bool IsNew { get; set; }

    /// <summary>The game division a new division's details are copied from; for a change, the division itself.</summary>
    public string StartsFrom { get; set; } = "";

    /// <summary>A new division's name; for a change, the name it is renamed to, or empty to keep the game's.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Whether a game division is taken out of the game's list of divisions, so players can no longer pick it; never
    /// for a new division, which is deleted from the mod instead.
    /// </summary>
    public bool IsRemoved { get; set; }

    // ---- its details, each null where it keeps the starting division's ----

    /// <summary>Its nation's code ("US"), and the side that nation is on ("NATO").</summary>
    public string? Country { get; set; }

    /// <inheritdoc cref="Country"/>
    public string? Side { get; set; }

    /// <summary>Its type's key: "ARMORED".</summary>
    public string? TypeToken { get; set; }

    /// <summary>The activation points a deck of it has.</summary>
    public int? ActivationPoints { get; set; }

    /// <summary>Its place in the game's list of divisions.</summary>
    public double? ListOrder { get; set; }

    /// <summary>Another of the game's emblems: its texture's name, and its picture as the game names it.</summary>
    public string? EmblemTexture { get; set; }

    /// <inheritdoc cref="EmblemTexture"/>
    public string? EmblemSource { get; set; }

    /// <summary>A picture of the user's own for its emblem, on this computer; it takes the place of the game's.</summary>
    public string? EmblemFile { get; set; }

    /// <summary>The units shown on its card in the game, by descriptor.</summary>
    public List<string>? StandoutUnits { get; set; }

    /// <summary>Its activation-point costs, by category: the cost of each card in turn.</summary>
    public Dictionary<string, List<int>>? Costs { get; set; }

    /// <summary>Whether a new division keeps the units of the division it starts from; a change always does.</summary>
    public bool KeepsUnits { get; set; } = true;

    /// <summary>
    /// The units added, with how the division has each; a unit the starting division has already is listed here when
    /// the mod changes how the division has it (its cards, units per card or veterancy).
    /// </summary>
    public List<DivisionUnit> Added { get; set; } = [];

    /// <summary>The units taken out of the division it starts from, by descriptor.</summary>
    public List<string> Removed { get; set; } = [];

    /// <summary>
    /// The division as the game will have it: the division it starts from, with this one's name and descriptor for a
    /// new division, the units removed taken out, the units it changes changed in their place, and the units added put
    /// in (after those kept, in the order added).
    /// </summary>
    public Division Apply(Division start)
    {
        IEnumerable<DivisionUnit> kept = KeepsUnits ? start.Units.Where(unit => !Removed.Contains(unit.Unit)) : [];
        List<DivisionUnit> units = [.. kept.Select(unit => Added.FirstOrDefault(added => added.Unit == unit.Unit) ?? unit)];
        units.AddRange(Added.Where(added => !units.Any(unit => unit.Unit == added.Unit)));
        Division applied = !IsNew
            ? start with { Units = units, Name = Name != "" ? Name : start.Name }
            : start with
            {
                Descriptor = Descriptor,
                CfgName = CfgNameOf(Descriptor),
                NameToken = "",
                Name = Name,
                IsForMultiplayer = true,
                RuleName = Descriptor + "_Rule",
                Units = units,
            };
        return WithDetails(applied);
    }

    /// <summary>A division with the details this one changes, and the rest as it has them.</summary>
    private Division WithDetails(Division division) => division with
    {
        Country = Country ?? division.Country,
        Side = Side ?? division.Side,
        TypeToken = TypeToken ?? division.TypeToken,
        ActivationPoints = ActivationPoints ?? division.ActivationPoints,
        ListOrder = ListOrder ?? division.ListOrder,
        EmblemTexture = EmblemFile is not null ? "" : EmblemTexture ?? division.EmblemTexture,
        EmblemSource = EmblemFile is not null ? null : EmblemSource ?? division.EmblemSource,
        EmblemFile = EmblemFile,
        StandoutUnits = StandoutUnits ?? division.StandoutUnits,
        Costs = Costs?.ToDictionary(cost => cost.Key, cost => (IReadOnlyList<int>)cost.Value) ?? division.Costs,
    };

    /// <summary>
    /// Lets go of each detail set back as the starting division has it, so a change that changes nothing else can go
    /// (see <see cref="ChangesNothing"/>).
    /// </summary>
    public void ForgetUnchangedDetails(Division start)
    {
        if (Country == start.Country && Side == start.Side) (Country, Side) = (null, null);
        if (TypeToken == start.TypeToken) TypeToken = null;
        if (ActivationPoints == start.ActivationPoints) ActivationPoints = null;
        if (ListOrder == start.ListOrder) ListOrder = null;
        if (EmblemTexture == start.EmblemTexture) (EmblemTexture, EmblemSource) = (null, null);
        if (StandoutUnits is not null && StandoutUnits.SequenceEqual(start.StandoutUnits)) StandoutUnits = null;
        if (Costs is not null && SameCosts(Costs, start.Costs)) Costs = null;
        if (!IsNew && Name == start.Name) Name = "";
    }

    /// <summary>Whether two divisions' costs are the same: the same categories, each with the same costs.</summary>
    private static bool SameCosts(Dictionary<string, List<int>> one, IReadOnlyDictionary<string, IReadOnlyList<int>> other) =>
        one.Count == other.Count && one.All(cost => other.TryGetValue(cost.Key, out IReadOnlyList<int>? costs) && costs.SequenceEqual(cost.Value));

    /// <summary>Whether it changes any of the division's details.</summary>
    private bool ChangesDetails =>
        Country is not null || Side is not null || TypeToken is not null || ActivationPoints is not null || ListOrder is not null
        || EmblemTexture is not null || EmblemSource is not null || EmblemFile is not null || StandoutUnits is not null || Costs is not null;

    /// <summary>Puts a unit in the division: back in when it was removed, else added; nothing when it is already in.</summary>
    /// <param name="unit">How the division is to have the unit.</param>
    /// <param name="start">The division it starts from, to know which units it already has.</param>
    /// <returns>Whether the division changed.</returns>
    public bool AddUnit(DivisionUnit unit, Division start)
    {
        if (Removed.Remove(unit.Unit)) return true;
        bool inStart = KeepsUnits && start.Units.Any(each => each.Unit == unit.Unit);
        if (inStart || Added.Any(each => each.Unit == unit.Unit)) return false;
        Added.Add(unit);
        return true;
    }

    /// <summary>
    /// Changes how the division has a unit (its cards, units per card or veterancy): kept in place of the starting
    /// division's, or of the one added; when it is as the starting division has it, the change is let go of.
    /// </summary>
    public void SetRule(DivisionUnit rule, Division start)
    {
        int index = Added.FindIndex(each => each.Unit == rule.Unit);
        DivisionUnit? inStart = KeepsUnits ? start.Units.FirstOrDefault(each => each.Unit == rule.Unit) : null;
        if (inStart is not null && IsSameRule(inStart, rule))
        {
            if (index >= 0) Added.RemoveAt(index);
            return;
        }
        if (index >= 0) Added[index] = rule;
        else Added.Add(rule);
    }

    /// <summary>Whether two rules give a unit the same way: the same cards, units per card, veterancy and transports.</summary>
    public static bool IsSameRule(DivisionUnit one, DivisionUnit other) =>
        one.Unit == other.Unit && one.Cards == other.Cards && one.UnitsPerCard == other.UnitsPerCard
        && one.UnitsPerCardByPhase.SequenceEqual(other.UnitsPerCardByPhase) && one.VeterancyMultipliers.SequenceEqual(other.VeterancyMultipliers)
        && one.AvailableWithoutTransport == other.AvailableWithoutTransport && one.Transports.SequenceEqual(other.Transports);

    /// <summary>
    /// Takes a unit out of the division: out of those added (with any change to how it is had), and out of those it
    /// starts with when the starting division has it.
    /// </summary>
    public void RemoveUnit(string unit, Division start)
    {
        Added.RemoveAll(each => each.Unit == unit);
        bool inStart = KeepsUnits && start.Units.Any(each => each.Unit == unit);
        if (inStart && !Removed.Contains(unit)) Removed.Add(unit);
    }

    /// <summary>Whether it changes nothing: a change to a game division that keeps its name, its units and its place in the list.</summary>
    public bool ChangesNothing => !IsNew && Name == "" && !IsRemoved && Added.Count == 0 && Removed.Count == 0 && !ChangesDetails;

    /// <summary>A copy that shares nothing with this one, so changing one leaves the other as it was.</summary>
    public ModDivision Copy() => FromJson(ToJson());

    /// <summary>
    /// A descriptor for a new division named <paramref name="name"/>: its letters and digits, with "_" between words,
    /// as the game names its own, and "_2", "_3"... when <paramref name="taken"/> has it already.
    /// </summary>
    public static string NewDescriptor(string name, IReadOnlyCollection<string> taken)
    {
        var letters = new System.Text.StringBuilder();
        foreach (char letter in name.Trim())
            letters.Append(char.IsAsciiLetterOrDigit(letter) ? letter : '_');
        string words = string.Join("_", letters.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (words == "") words = "Division";
        string descriptor = "Descriptor_Deck_Division_" + words + "_multi";
        for (int number = 2; taken.Contains(descriptor); number++)
            descriptor = "Descriptor_Deck_Division_" + words + "_" + number + "_multi";
        return descriptor;
    }

    /// <summary>A descriptor's internal name: "Descriptor_Deck_Division_My_Division_multi" is "My_Division_multi".</summary>
    private static string CfgNameOf(string descriptor) =>
        descriptor.StartsWith("Descriptor_Deck_Division_", StringComparison.Ordinal) ? descriptor["Descriptor_Deck_Division_".Length..] : descriptor;

    // ---- the mod file ----

    /// <summary>The division as an entry of the mod file's <c>Divisions</c>.</summary>
    public JsonObject ToJson() => new()
    {
        ["Descriptor"] = Descriptor,
        ["IsNew"] = IsNew,
        ["StartsFrom"] = StartsFrom,
        ["Name"] = Name,
        ["IsRemoved"] = IsRemoved,
        ["KeepsUnits"] = KeepsUnits,
        ["Added"] = new JsonArray([.. Added.Select(UnitToJson)]),
        ["Removed"] = new JsonArray([.. Removed.Select(unit => (JsonNode)JsonValue.Create(unit))]),
        ["Details"] = DetailsToJson(),
    };

    /// <summary>The details it changes, each only when it changes it.</summary>
    private JsonObject DetailsToJson()
    {
        var json = new JsonObject();
        if (Country is not null) json["Country"] = Country;
        if (Side is not null) json["Side"] = Side;
        if (TypeToken is not null) json["TypeToken"] = TypeToken;
        if (ActivationPoints is int points) json["ActivationPoints"] = points;
        if (ListOrder is double order) json["ListOrder"] = order;
        if (EmblemTexture is not null) json["EmblemTexture"] = EmblemTexture;
        if (EmblemSource is not null) json["EmblemSource"] = EmblemSource;
        if (EmblemFile is not null) json["EmblemFile"] = EmblemFile;
        if (StandoutUnits is not null) json["StandoutUnits"] = new JsonArray([.. StandoutUnits.Select(unit => (JsonNode)JsonValue.Create(unit))]);
        if (Costs is not null)
        {
            var costs = new JsonObject();
            foreach ((string category, List<int> each) in Costs) costs[category] = new JsonArray([.. each.Select(cost => (JsonNode)JsonValue.Create(cost))]);
            json["Costs"] = costs;
        }
        return json;
    }

    /// <summary>Reads the details an entry changes into <paramref name="division"/>.</summary>
    private static void DetailsFromJson(JsonObject? json, ModDivision division)
    {
        if (json is null) return;
        division.Country = Text(json["Country"]);
        division.Side = Text(json["Side"]);
        division.TypeToken = Text(json["TypeToken"]);
        division.ActivationPoints = WholeNumber(json["ActivationPoints"]);
        division.ListOrder = Number(json["ListOrder"]);
        division.EmblemTexture = Text(json["EmblemTexture"]);
        division.EmblemSource = Text(json["EmblemSource"]);
        division.EmblemFile = Text(json["EmblemFile"]);
        if (json["StandoutUnits"] is JsonArray standouts) division.StandoutUnits = [.. standouts.Select(JsonValues.AsText).Where(unit => unit != "")];
        if (json["Costs"] is JsonObject costs)
            division.Costs = costs.ToDictionary(cost => cost.Key, cost => (cost.Value as JsonArray ?? []).Select(each => WholeNumber(each) ?? 0).ToList());
    }

    /// <summary>A text of the mod file; null where it has none.</summary>
    private static string? Text(JsonNode? node) => node is null ? null : JsonValues.AsText(node);

    /// <summary>Reads an entry of the mod file's <c>Divisions</c>; what it does not have is left as at first.</summary>
    public static ModDivision FromJson(JsonObject json)
    {
        ModDivision division = BasicsFromJson(json);
        DetailsFromJson(json["Details"] as JsonObject, division);
        return division;
    }

    /// <summary>What an entry of the mod file keeps besides the details.</summary>
    private static ModDivision BasicsFromJson(JsonObject json) => new()
    {
        Descriptor = JsonValues.AsText(json["Descriptor"]),
        IsNew = JsonValues.IsTruthy(json["IsNew"]),
        StartsFrom = JsonValues.AsText(json["StartsFrom"]),
        Name = JsonValues.AsText(json["Name"]),
        IsRemoved = JsonValues.IsTruthy(json["IsRemoved"]),
        KeepsUnits = !JsonValues.IsFalse(json["KeepsUnits"]),
        Added = [.. (json["Added"] as JsonArray ?? []).OfType<JsonObject>().Select(UnitFromJson)],
        Removed = [.. (json["Removed"] as JsonArray ?? []).Select(JsonValues.AsText).Where(unit => unit != "")],
    };

    private static JsonNode UnitToJson(DivisionUnit unit)
    {
        var json = new JsonObject { ["Unit"] = unit.Unit, ["Cards"] = unit.Cards };
        if (unit.UnitsPerCard is int perCard) json["UnitsPerCard"] = perCard;
        if (unit.UnitsPerCardByPhase.Count > 0) json["UnitsPerCardByPhase"] = new JsonArray([.. unit.UnitsPerCardByPhase.Select(count => (JsonNode)JsonValue.Create(count))]);
        json["VeterancyMultipliers"] = new JsonArray([.. unit.VeterancyMultipliers.Select(share => (JsonNode)JsonValue.Create(share))]);
        json["AvailableWithoutTransport"] = unit.AvailableWithoutTransport;
        json["Transports"] = new JsonArray([.. unit.Transports.Select(transport => (JsonNode)JsonValue.Create(transport))]);
        return json;
    }

    private static DivisionUnit UnitFromJson(JsonObject json) => new()
    {
        Unit = JsonValues.AsText(json["Unit"]),
        Cards = WholeNumber(json["Cards"]) ?? 1,
        UnitsPerCard = WholeNumber(json["UnitsPerCard"]),
        UnitsPerCardByPhase = [.. (json["UnitsPerCardByPhase"] as JsonArray ?? []).Select(count => WholeNumber(count) ?? 0)],
        VeterancyMultipliers = [.. (json["VeterancyMultipliers"] as JsonArray ?? []).Select(share => Number(share) ?? 0)],
        AvailableWithoutTransport = !JsonValues.IsFalse(json["AvailableWithoutTransport"]),
        Transports = [.. (json["Transports"] as JsonArray ?? []).Select(JsonValues.AsText).Where(transport => transport != "")],
    };

    private static int? WholeNumber(JsonNode? node) => Number(node) is double number ? (int)Math.Round(number) : null;

    /// <summary>
    /// A number, read from a file or made in code: a value made from a whole number in code is not read as a double,
    /// so it is tried as one first.
    /// </summary>
    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out int whole)) return whole;
        return value.TryGetValue(out double number) ? number : null;
    }
}
