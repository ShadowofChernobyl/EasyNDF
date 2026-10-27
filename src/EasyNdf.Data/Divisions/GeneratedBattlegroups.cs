using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The Official Battlegroups of generated divisions whose battlegroup is generated too, made at export: the rules that
/// add the deck and its cards (<see cref="DivisionChangeRules.GeneratedBattlegroupKey"/>) are given the cards the
/// division's Generation Settings choose from its units, as the export makes them (<see cref="BattlegroupGenerator"/>).
/// </summary>
public static partial class GeneratedBattlegroups
{
    /// <summary>The part of a generated battlegroup a rule adds: its deck, in Decks.ndf.</summary>
    public const string DeckPart = "Deck";

    /// <summary>The part of a generated battlegroup a rule adds: its cards, in DeckPacks.ndf.</summary>
    public const string CardsPart = "Cards";

    /// <summary>What a generated battlegroup's rule keeps: the division's entry, the part it adds, and the deck's name token.</summary>
    internal static JsonObject PartJson(ModDivision change, string part, string token) => new()
    {
        ["Part"] = part,
        ["Token"] = token,
        ["Entry"] = change.ToJson(),
    };

    /// <summary>Whether a rule adds a part of a generated battlegroup.</summary>
    public static bool IsGeneratedBattlegroupRule(JsonNode? rule) =>
        rule?[DivisionChangeRules.ArmoryKey]?[DivisionChangeRules.GeneratedBattlegroupKey] is JsonObject;

    /// <summary>Whether any of the rules adds a part of a generated battlegroup.</summary>
    public static bool AnyIn(IEnumerable<JsonNode?> rules) => rules.Any(IsGeneratedBattlegroupRule);

    /// <summary>The divisions' entries the rules make battlegroups for, for the checksum of what they are made from.</summary>
    public static IEnumerable<JsonObject> EntriesIn(IEnumerable<JsonNode?> rules) =>
        rules.Where(IsGeneratedBattlegroupRule).Select(rule => (JsonObject)rule![DivisionChangeRules.ArmoryKey]![DivisionChangeRules.GeneratedBattlegroupKey]!["Entry"]!);

    /// <summary>
    /// The cards of each generated battlegroup the rules are for, by the division's descriptor: the division's units
    /// as its generation and the user's changes make them, then the cards its Generation Settings choose.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<BattlegroupCard>> CardsOf(IEnumerable<JsonNode?> rules, DivisionRosters rosters)
    {
        var battlegroups = new Dictionary<string, IReadOnlyList<BattlegroupCard>>(StringComparer.Ordinal);
        foreach (JsonObject json in EntriesIn(rules))
        {
            ModDivision entry = ModDivision.FromJson(json);
            if (entry.BattlegroupGeneration is not BattlegroupGeneration settings || battlegroups.ContainsKey(entry.Descriptor)) continue;
            // the frame its file entries are copied from plays no part in its units
            var frame = new Division { Descriptor = entry.StartsFrom, Name = entry.Name };
            Division division = entry.Apply(rosters.StartOf(entry, frame));
            battlegroups[entry.Descriptor] = [.. BattlegroupGenerator.Cards(division, rosters.UnitsByDescriptor, settings).Select(card => card.Card)];
        }
        return battlegroups;
    }

    /// <summary>
    /// Gives each generated battlegroup's rules their text: the deck, with its cards in order; and the cards DeckPacks.ndf
    /// does not have yet (neither the file nor another rule adds them), each once. A battlegroup with no cards has its
    /// rules switched off: the game takes no empty deck.
    /// </summary>
    /// <param name="rules">The rules to fill, changed in place.</param>
    /// <param name="battlegroups">The cards of each battlegroup, by its division's descriptor (<see cref="CardsOf"/>).</param>
    /// <param name="packsInFile">The cards DeckPacks.ndf has, as the mods' rules make it.</param>
    public static void Fill(IEnumerable<JsonNode?> rules, IReadOnlyDictionary<string, IReadOnlyList<BattlegroupCard>> battlegroups, IReadOnlySet<string> packsInFile)
    {
        List<JsonNode?> all = [.. rules];
        HashSet<string> packsAdded = [.. packsInFile, .. PacksAddedByOtherRules(all)];
        foreach (JsonNode? rule in all.Where(IsGeneratedBattlegroupRule))
        {
            JsonObject part = (JsonObject)rule![DivisionChangeRules.ArmoryKey]![DivisionChangeRules.GeneratedBattlegroupKey]!;
            string division = JsonValues.AsText(part["Entry"]?["Descriptor"]);
            if (!battlegroups.TryGetValue(division, out IReadOnlyList<BattlegroupCard>? cards)) continue;
            string text = JsonValues.AsText(part["Part"]) == DeckPart
                ? DivisionChangeRules.DeckText(ModDivision.DeckDescriptorOf(division), division, JsonValues.AsText(part["Token"]), cards)
                : string.Join("\n", cards.Where(card => packsAdded.Add(card.PackName)).Select(DivisionChangeRules.CardText));
            rule["Params"]!["Text"] = text;
            rule["Enabled"] = cards.Count > 0 && text != "";
        }
    }

    /// <summary>The cards other rules add to DeckPacks.ndf (the battlegroups of other new divisions), by their descriptors.</summary>
    private static IEnumerable<string> PacksAddedByOtherRules(IEnumerable<JsonNode?> rules) =>
        rules.Where(rule => !IsGeneratedBattlegroupRule(rule) && JsonValues.AsText(rule?["Kind"]) == "Add")
            .SelectMany(rule => PackDeclaration().Matches(JsonValues.AsText(rule?["Params"]?["Text"])).Select(match => match.Groups[1].Value));

    /// <summary>A card's declaration in DeckPacks.ndf: "Descriptor_Deck_Pack_X is DeckPackDescriptor".</summary>
    [GeneratedRegex(@"(Descriptor_Deck_Pack_[A-Za-z0-9_]+)[ \t]+is[ \t]+DeckPackDescriptor")]
    private static partial Regex PackDeclaration();
}
