using EasyNdf.Data.Game;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Ndf;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// Reads the game's (or mod's) Official Battlegroups: the decks of Decks.ndf, each naming its division and its cards,
/// and the cards themselves from DeckPacks.ndf. Decks.ndf also holds the decks of the game's scripted missions; those
/// name stand-in divisions no player can pick, and the caller leaves them out.
/// </summary>
internal static class BattlegroupReader
{
    private const string DecksFolder = "Generated/Gameplay/Decks/";

    /// <summary>
    /// Every deck, with the division it names, in the order of Decks.ndf. A card the deck names but DeckPacks.ndf does
    /// not have is left out, and noted in <paramref name="problems"/>. Nothing when the files are missing (Steel
    /// Division 2 keeps its decks elsewhere).
    /// </summary>
    /// <param name="cardNames">When given, it gets the descriptor of every card in DeckPacks.ndf.</param>
    public static List<(string Division, Battlegroup Battlegroup)> Read(GameDataFiles files, GameTexts texts, List<string> problems, HashSet<string>? cardNames = null)
    {
        var decks = new List<(string, Battlegroup)>();
        Dictionary<string, BattlegroupCard>? cards = null;
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "Decks.ndf", _ => true, problems, mustExist: false))
        {
            if (declaration.Value is not ObjectValue { TypeName: "TDeckDescriptor" } deck) continue;
            if (Text(Own(deck, "DeckDivision")) is not string division || Own(deck, "DeckPackList") is not ListValue packs) continue;
            // read only once a deck is found, as the file is long
            cards ??= ReadCards(files, problems);
            decks.Add((division, ReadDeck(declaration.Name, deck, packs, cards, texts, problems)));
        }
        if (cardNames is not null) cardNames.UnionWith((cards ?? ReadCards(files, problems)).Keys);
        return decks;
    }

    private static Battlegroup ReadDeck(
        string descriptor, ObjectValue deck, ListValue packs, Dictionary<string, BattlegroupCard> cards, GameTexts texts, List<string> problems)
    {
        var deckCards = new List<BattlegroupCard>();
        foreach (NdfValue pack in packs.Items)
        {
            if (Text(pack) is not string name) continue;
            if (cards.TryGetValue(name, out BattlegroupCard? card)) deckCards.Add(card);
            else problems.Add("Decks.ndf, " + descriptor + ": " + name + " is not in DeckPacks.ndf");
        }
        string nameToken = Text(Own(deck, "DeckName")) ?? "";
        return new Battlegroup
        {
            Descriptor = descriptor,
            NameToken = nameToken,
            Name = texts.Get(nameToken) ?? "",
            Cards = deckCards,
        };
    }

    /// <summary>
    /// Every card of DeckPacks.ndf, by its descriptor. The game writes them with its DeckPackDescriptor template
    /// (Unit, Transport, Xp); the names the template fills in (UnitDescriptor, TransporterDescriptor, ExperienceLevel)
    /// are read too, for a card written without it.
    /// </summary>
    private static Dictionary<string, BattlegroupCard> ReadCards(GameDataFiles files, List<string> problems)
    {
        var cards = new Dictionary<string, BattlegroupCard>(StringComparer.Ordinal);
        foreach (Declaration declaration in UnitReader.Declarations(files, DecksFolder + "DeckPacks.ndf",
            name => name.StartsWith("Descriptor_Deck_Pack_", StringComparison.Ordinal), problems, mustExist: false))
        {
            if (declaration.Value is not ObjectValue pack) continue;
            string? unit = Text(Own(pack, "Unit")) ?? Text(Own(pack, "UnitDescriptor"));
            if (unit is null) continue;
            string? transport = Text(Own(pack, "Transport")) ?? Text(Own(pack, "TransporterDescriptor"));
            double veterancy = Number(Own(pack, "Xp")) ?? Number(Own(pack, "ExperienceLevel")) ?? 0;
            cards.TryAdd(declaration.Name, new BattlegroupCard(unit, (int)veterancy, transport));
        }
        return cards;
    }
}
