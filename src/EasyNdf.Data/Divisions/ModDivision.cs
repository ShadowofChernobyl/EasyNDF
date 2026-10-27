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

    /// <summary>Its summary, the few lines the game's armory shows about it.</summary>
    public string? Summary { get; set; }

    /// <summary>Its history, the longer text the game's armory shows about it.</summary>
    public string? History { get; set; }

    /// <summary>The activation points a deck of it has.</summary>
    public int? ActivationPoints { get; set; }

    /// <summary>Its place in the game's list of divisions.</summary>
    public double? ListOrder { get; set; }

    /// <summary>
    /// Another of the game's emblems: its texture's name, and its picture as the game names it. Both "" for no emblem
    /// (an empty placeholder), as a new division starts.
    /// </summary>
    public string? EmblemTexture { get; set; }

    /// <inheritdoc cref="EmblemTexture"/>
    public string? EmblemSource { get; set; }

    /// <summary>A picture of the user's own for its emblem, on this computer; it takes the place of the game's.</summary>
    public string? EmblemFile { get; set; }

    /// <summary>
    /// A new division's number in deck codes, given once when it is made and kept, so the decks players save with it
    /// stay valid from one export to the next. Null for a game division, which keeps its own.
    /// </summary>
    public int? DeckNumber { get; set; }

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
    /// The cards of its Official Battlegroup, in order; null to keep the game's (a new division then has none).
    /// </summary>
    public List<BattlegroupCard>? BattlegroupCards { get; set; }

    /// <summary>Its Official Battlegroup's name; empty to keep the game's (a new division's is then made from its own).</summary>
    public string BattlegroupName { get; set; } = "";

    /// <summary>
    /// For a generated division, how it is made (<see cref="DivisionGeneration"/>); null for any other. Its units are
    /// then not kept: they are made each time (<see cref="DivisionRosters.StartOf"/>), and <see cref="Added"/> and
    /// <see cref="Removed"/> hold only the user's own changes to them.
    /// </summary>
    public DivisionGeneration? Generation { get; set; }

    /// <summary>Whether it is a generated division (<see cref="Generation"/>).</summary>
    public bool IsGenerated => Generation is not null;

    /// <summary>
    /// For a generated division whose Official Battlegroup is generated too, how it is made (its Generation Settings,
    /// <see cref="BattlegroupGenerator"/>); null for a battlegroup of fixed cards (<see cref="BattlegroupCards"/>). Its
    /// cards are made again each time, from the division's units.
    /// </summary>
    public BattlegroupGeneration? BattlegroupGeneration { get; set; }

    /// <summary>
    /// "Make Fixed": a generated division becomes an ordinary new division, with the units it has now (those made, with
    /// the user's changes); it then no longer follows the files.
    /// </summary>
    /// <param name="generatedStart">Its start with the units its generation gives it (<see cref="DivisionRosters.StartOf"/>).</param>
    public void MakeFixed(Division generatedStart)
    {
        if (Generation is null) return;
        Added = [.. Apply(generatedStart).Units];
        Removed = [];
        KeepsUnits = false;
        Generation = null;
    }

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
        return WithDetails(applied) with { Battlegroup = BattlegroupOf(start) };
    }

    /// <summary>
    /// The deck of a new division, in Decks.ndf: "Descriptor_Deck_" and its internal name, as the game names its own
    /// ("Descriptor_Deck_My_Division_multi").
    /// </summary>
    public static string DeckDescriptorOf(string division) => "Descriptor_Deck_" + CfgNameOf(division);

    /// <summary>
    /// The Official Battlegroup as the game will have it: the starting division's with this one's cards and name; a new
    /// division's own, once it has cards; none when there is neither.
    /// </summary>
    private Battlegroup? BattlegroupOf(Division start)
    {
        if (IsNew)
        {
            if (BattlegroupCards is not { Count: > 0 } cards) return null;
            return new Battlegroup { Descriptor = DeckDescriptorOf(Descriptor), Name = BattlegroupName, Cards = [.. cards] };
        }
        if (start.Battlegroup is not Battlegroup game) return null;
        return game with
        {
            Cards = BattlegroupCards is null ? game.Cards : [.. BattlegroupCards],
            Name = BattlegroupName != "" ? BattlegroupName : game.Name,
        };
    }

    /// <summary>
    /// The tags a division has with another nation, side or type: those it had in place of the old ones, the rest
    /// kept, as ['DEFAULT', 'US', 'NATO', 'ARMORED']. A type of none leaves no type tag; DEFAULT (multiplayer) is kept.
    /// </summary>
    public static List<string> TagsWith(Division before, string country, string side, string type)
    {
        List<string> tags = [.. before.Tags
            .Select(tag => tag == before.Country ? country : tag == before.Side ? side : tag == before.TypeToken && before.TypeToken != "" ? type : tag)
            .Where(tag => tag != "")];
        if (!tags.Contains("DEFAULT")) tags.Insert(0, "DEFAULT");
        return tags;
    }

    /// <summary>
    /// A new division's tags, its own and none of the division it was made from: DEFAULT (multiplayer), its nation, its
    /// side, and its type when it has one.
    /// </summary>
    public static List<string> TagsOfNew(string country, string side, string type) =>
        [.. new[] { "DEFAULT", country, side, type }.Where(tag => tag != "")];

    /// <summary>A division with the details this one changes, and the rest as it has them.</summary>
    private Division WithDetails(Division division) => division with
    {
        Tags = IsNew
            ? TagsOfNew(Country ?? division.Country, Side ?? division.Side, TypeToken ?? division.TypeToken)
            : TagsWith(division, Country ?? division.Country, Side ?? division.Side, TypeToken ?? division.TypeToken),
        Country = Country ?? division.Country,
        Side = Side ?? division.Side,
        TypeToken = TypeToken ?? division.TypeToken,
        // a new division's texts are its own: never those of the division it was made from
        Summary = Summary ?? (IsNew ? "" : division.Summary),
        History = History ?? (IsNew ? "" : division.History),
        ActivationPoints = ActivationPoints ?? division.ActivationPoints,
        ListOrder = ListOrder ?? division.ListOrder,
        EmblemTexture = EmblemFile is not null ? "" : EmblemTexture ?? division.EmblemTexture,
        EmblemSource = EmblemFile is not null ? null : EmblemSource ?? division.EmblemSource,
        EmblemFile = EmblemFile,
        StandoutUnits = StandoutUnits ?? division.StandoutUnits,
        Costs = WithCosts(division.Costs),
    };

    /// <summary>
    /// The division's costs with the categories this one changes in place of its own, the rest kept, as the
    /// division's file will have them.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyList<int>> WithCosts(IReadOnlyDictionary<string, IReadOnlyList<int>> costs)
    {
        if (Costs is null) return costs;
        var merged = costs.ToDictionary(cost => cost.Key, cost => cost.Value, StringComparer.Ordinal);
        foreach ((string category, List<int> each) in Costs) merged[category] = each;
        return merged;
    }

    /// <summary>
    /// Lets go of each detail set back as the starting division has it, so a change that changes nothing else can go
    /// (see <see cref="ChangesNothing"/>).
    /// </summary>
    public void ForgetUnchangedDetails(Division start)
    {
        // a new division's details are its own: let go of, they would become those of the division it was made from
        if (IsNew) return;
        if (Country == start.Country && Side == start.Side) (Country, Side) = (null, null);
        if (TypeToken == start.TypeToken) TypeToken = null;
        if (Summary == start.Summary) Summary = null;
        if (History == start.History) History = null;
        if (ActivationPoints == start.ActivationPoints) ActivationPoints = null;
        if (ListOrder == start.ListOrder) ListOrder = null;
        if (EmblemTexture == start.EmblemTexture) (EmblemTexture, EmblemSource) = (null, null);
        if (StandoutUnits is not null && StandoutUnits.SequenceEqual(start.StandoutUnits)) StandoutUnits = null;
        if (Costs is not null && SameCosts(Costs, start.Costs)) Costs = null;
        if (!IsNew && Name == start.Name) Name = "";
        if (BattlegroupCards is not null && start.Battlegroup is not null && BattlegroupCards.SequenceEqual(start.Battlegroup.Cards)) BattlegroupCards = null;
        if (start.Battlegroup is not null && BattlegroupName == start.Battlegroup.Name) BattlegroupName = "";
    }

    /// <summary>Whether two divisions' costs are the same: the same categories, each with the same costs.</summary>
    private static bool SameCosts(Dictionary<string, List<int>> one, IReadOnlyDictionary<string, IReadOnlyList<int>> other) =>
        one.Count == other.Count && one.All(cost => other.TryGetValue(cost.Key, out IReadOnlyList<int>? costs) && costs.SequenceEqual(cost.Value));

    /// <summary>Whether it changes any of the division's details.</summary>
    private bool ChangesDetails =>
        Country is not null || Side is not null || TypeToken is not null || Summary is not null || History is not null || ActivationPoints is not null || ListOrder is not null
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

    /// <summary>
    /// Lets go of one change, as Make Editable does when its rule is kept by hand instead
    /// (<see cref="DivisionChangeRules.ChangeKey"/>): the division has that value as its starting division has it again.
    /// A new division's changes cannot be let go of one by one; the caller deletes the division.
    /// </summary>
    /// <param name="change">The change, such as "Points", "Costs:Logistic" or "Unit:Descriptor_Unit_X:Cards".</param>
    /// <param name="start">The division it starts from.</param>
    public void LetGoOf(string change, Division start)
    {
        string[] parts = change.Split(':');
        switch (parts[0])
        {
            case "Name": Name = ""; break;
            case "TakenOut": IsRemoved = false; break;
            case "Points": ActivationPoints = null; break;
            case "ListOrder": ListOrder = null; break;
            case "Type": TypeToken = null; break;
            case "Nation": (Country, Side) = (null, null); break;
            case "Standout": StandoutUnits = null; break;
            case "Summary": Summary = null; break;
            case "History": History = null; break;
            case "Emblem": (EmblemTexture, EmblemSource, EmblemFile) = (null, null, null); break;
            case "Costs":
                Costs?.Remove(parts[1]);
                if (Costs is { Count: 0 }) Costs = null;
                break;
            case "UnitOut": Removed.Remove(parts[1]); break;
            case "UnitIn": Added.RemoveAll(unit => unit.Unit == parts[1]); break;
            case "Unit": LetGoOfUnitValue(parts[1], parts[2], start); break;
            case "Battlegroup": BattlegroupCards = null; break;
            case "BattlegroupName": BattlegroupName = ""; break;
        }
    }

    /// <summary>Gives a unit one of its values back as the starting division has it, such as its cards.</summary>
    private void LetGoOfUnitValue(string unit, string value, Division start)
    {
        int index = Added.FindIndex(each => each.Unit == unit);
        DivisionUnit? before = start.Units.FirstOrDefault(each => each.Unit == unit);
        if (index < 0 || before is null) return;
        DivisionUnit now = Added[index];
        DivisionUnit back = value switch
        {
            "Cards" => now with { Cards = before.Cards },
            "UnitsPerCard" => now with { UnitsPerCard = before.UnitsPerCard },
            "Veterancy" => now with { VeterancyMultipliers = before.VeterancyMultipliers },
            "NoTransport" => now with { AvailableWithoutTransport = before.AvailableWithoutTransport },
            "Transports" => now with { Transports = before.Transports },
            _ => now,
        };
        SetRule(back, start);
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
    public bool ChangesNothing =>
        !IsNew && Name == "" && !IsRemoved && Added.Count == 0 && Removed.Count == 0 && !ChangesDetails && BattlegroupCards is null && BattlegroupName == "";

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
        ["DeckNumber"] = DeckNumber,
        ["Added"] = new JsonArray([.. Added.Select(UnitToJson)]),
        ["Removed"] = new JsonArray([.. Removed.Select(unit => (JsonNode)JsonValue.Create(unit))]),
        ["Details"] = DetailsToJson(),
        ["Battlegroup"] = BattlegroupToJson(),
        ["Generation"] = Generation?.ToJson(),
        ["BattlegroupGeneration"] = BattlegroupGeneration?.ToJson(),
    };

    /// <summary>The Official Battlegroup's changes: its name and cards, each only when it changes them; null for none.</summary>
    private JsonObject? BattlegroupToJson()
    {
        if (BattlegroupCards is null && BattlegroupName == "") return null;
        var json = new JsonObject();
        if (BattlegroupName != "") json["Name"] = BattlegroupName;
        if (BattlegroupCards is not null)
            json["Cards"] = new JsonArray([.. BattlegroupCards.Select(card => (JsonNode)new JsonObject
            {
                ["Unit"] = card.Unit, ["Veterancy"] = card.Veterancy, ["Transport"] = card.Transport,
            })]);
        return json;
    }

    /// <summary>Reads the Official Battlegroup's changes into <paramref name="division"/>.</summary>
    private static void BattlegroupFromJson(JsonObject? json, ModDivision division)
    {
        if (json is null) return;
        division.BattlegroupName = Text(json["Name"]) ?? "";
        if (json["Cards"] is JsonArray cards)
            division.BattlegroupCards = [.. cards.OfType<JsonObject>()
                .Select(card => new BattlegroupCard(JsonValues.AsText(card["Unit"]), WholeNumber(card["Veterancy"]) ?? 0, Text(card["Transport"])))
                .Where(card => card.Unit != "")];
    }

    /// <summary>The details it changes, each only when it changes it.</summary>
    private JsonObject DetailsToJson()
    {
        var json = new JsonObject();
        if (Country is not null) json["Country"] = Country;
        if (Side is not null) json["Side"] = Side;
        if (TypeToken is not null) json["TypeToken"] = TypeToken;
        if (Summary is not null) json["Summary"] = Summary;
        if (History is not null) json["History"] = History;
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
        division.Summary = Text(json["Summary"]);
        division.History = Text(json["History"]);
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
        BattlegroupFromJson(json["Battlegroup"] as JsonObject, division);
        if (json["Generation"] is JsonObject generation) division.Generation = DivisionGeneration.FromJson(generation);
        if (json["BattlegroupGeneration"] is JsonObject battlegroup) division.BattlegroupGeneration = BattlegroupGeneration.FromJson(battlegroup);
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
        DeckNumber = WholeNumber(json["DeckNumber"]),
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
