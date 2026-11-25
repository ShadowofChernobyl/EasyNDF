using System.Text.Json.Nodes;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The open mod's change to one of WARNO's Armory tabs (a unit's FactoryType, such as "Support"): the label on the tab,
/// the three texts of its tooltip, and, for a tab the game shows none for, the cost row its divisions get. Only what
/// the user gives is kept. The Armory's Divisions preset writes it as locked rules (<see cref="TabChangeRules"/>).
/// </summary>
public sealed class ModTab
{
    /// <summary>The tab, as the files name it without "Factory/": "Support".</summary>
    public string Factory { get; set; } = "";

    /// <summary>The label on the tab, such as "SUP"; null to keep the game's.</summary>
    public string? Label { get; set; }

    /// <summary>The tooltip's title; null to keep the game's.</summary>
    public string? TipTitle { get; set; }

    /// <summary>The tooltip's text; null to keep the game's.</summary>
    public string? TipText { get; set; }

    /// <summary>The tooltip's longer text; null to keep the game's.</summary>
    public string? TipMore { get; set; }

    /// <summary>
    /// The cost of each card slot a division gets for the tab, cheapest first, as a row of DivisionCostMatrix.ndf; null
    /// for a tab the divisions have rows for already.
    /// </summary>
    public List<int>? Slots { get; set; }

    /// <summary>
    /// A division's own cost row for the tab, by the division's descriptor, set when the tab is edited with that
    /// division shown; the others keep <see cref="Slots"/>. Null for none.
    /// </summary>
    public Dictionary<string, List<int>>? SlotsFor { get; set; }

    /// <summary>The cost row a division gets: its own, else the tab's.</summary>
    public List<int>? SlotsOf(string division) => SlotsFor is not null && SlotsFor.TryGetValue(division, out List<int>? own) ? own : Slots;

    /// <summary>Whether it changes nothing, so it can be let go of.</summary>
    public bool ChangesNothing => Label is null && TipTitle is null && TipText is null && TipMore is null && Slots is null && SlotsFor is not { Count: > 0 };

    /// <summary>The mod's change to a tab; null when it changes none.</summary>
    public static ModTab? Of(Mod mod, string factory) => mod.Tabs.FirstOrDefault(tab => tab.Factory == factory);

    /// <summary>A copy that shares nothing with this one.</summary>
    public ModTab Copy() => FromJson(ToJson());

    /// <summary>The change as an entry of the mod file's <c>Tabs</c>; what is kept from the game is left out.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["Factory"] = Factory };
        if (Label is not null) json["Label"] = Label;
        if (TipTitle is not null) json["TipTitle"] = TipTitle;
        if (TipText is not null) json["TipText"] = TipText;
        if (TipMore is not null) json["TipMore"] = TipMore;
        if (Slots is not null) json["Slots"] = new JsonArray([.. Slots.Select(slot => (JsonNode)slot)]);
        if (SlotsFor is { Count: > 0 })
        {
            var own = new JsonObject();
            foreach (var (division, slots) in SlotsFor) own[division] = new JsonArray([.. slots.Select(slot => (JsonNode)slot)]);
            json["SlotsFor"] = own;
        }
        return json;
    }

    /// <summary>Reads an entry of the mod file's <c>Tabs</c>.</summary>
    public static ModTab FromJson(JsonObject json)
    {
        static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        return new ModTab
        {
            Factory = Text(json["Factory"]) ?? "",
            Label = Text(json["Label"]),
            TipTitle = Text(json["TipTitle"]),
            TipText = Text(json["TipText"]),
            TipMore = Text(json["TipMore"]),
            Slots = json["Slots"] is JsonArray slots ? Costs(slots) : null,
            SlotsFor = json["SlotsFor"] is JsonObject own && own.Count > 0
                ? own.Where(each => each.Value is JsonArray).ToDictionary(each => each.Key, each => Costs((JsonArray)each.Value!), StringComparer.Ordinal)
                : null,
        };

        static List<int> Costs(JsonArray slots) => [.. slots.Select(slot => slot is JsonValue value && value.TryGetValue(out int cost) ? cost : 1)];
    }
}
