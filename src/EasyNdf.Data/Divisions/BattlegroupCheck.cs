namespace EasyNdf.Data.Divisions;

/// <summary>What is wrong with a battlegroup, by the rules of its division.</summary>
public enum BattlegroupProblemKind
{
    /// <summary>A card's unit is not one the division has.</summary>
    UnitNotInDivision,

    /// <summary>A card has no transport, and the division has its unit only in one.</summary>
    NeedsTransport,

    /// <summary>A card's transport is not one the division lets its unit ride in.</summary>
    TransportNotAllowed,

    /// <summary>A card's veterancy level is one the division does not offer for its unit.</summary>
    VeterancyNotOffered,

    /// <summary>More cards of one unit than the division allows.</summary>
    TooManyCardsOfUnit,

    /// <summary>More cards of one type (category) than the division has costs for.</summary>
    TooManyCardsOfType,

    /// <summary>The cards cost more activation points than the division has.</summary>
    TooManyPoints,
}

/// <summary>One thing wrong with a battlegroup.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Cards">The cards it is about, by their place in the battlegroup; none for the points.</param>
/// <param name="Subject">The unit's descriptor, or the type ("Tanks") for <see cref="BattlegroupProblemKind.TooManyCardsOfType"/>.</param>
/// <param name="Count">How many there are: cards, points, or the veterancy level for <see cref="BattlegroupProblemKind.VeterancyNotOffered"/>.</param>
/// <param name="Allowed">How many the division allows.</param>
/// <param name="Transport">The transport, for <see cref="BattlegroupProblemKind.TransportNotAllowed"/>.</param>
public sealed record BattlegroupProblem(
    BattlegroupProblemKind Kind, IReadOnlyList<int> Cards, string Subject = "", int Count = 0, int Allowed = 0, string? Transport = null);

/// <summary>
/// Checks a battlegroup against its division's rules, as the game does: GenerateMod stops on a deck whose card does
/// not match its division's rule, so the Armory shows these before the export.
/// </summary>
public static class BattlegroupCheck
{
    /// <summary>Everything wrong with the cards, the cards' own problems first, in the order of the cards.</summary>
    /// <param name="cards">The battlegroup's cards.</param>
    /// <param name="division">The division, as the mod has it.</param>
    /// <param name="typeOf">A unit's type (its factory, "Tanks"), by its descriptor; null when the unit is not known.</param>
    public static List<BattlegroupProblem> Problems(IReadOnlyList<BattlegroupCard> cards, Division division, Func<string, string?> typeOf)
    {
        var problems = new List<BattlegroupProblem>();
        for (int place = 0; place < cards.Count; place++)
            if (CardProblem(cards[place], place, division) is BattlegroupProblem problem) problems.Add(problem);
        problems.AddRange(TooManyOfAUnit(cards, division));
        problems.AddRange(TooManyOfAType(cards, division, typeOf));
        int points = Points(cards, division, typeOf);
        if (division.ActivationPoints is int allowed && points > allowed)
            problems.Add(new BattlegroupProblem(BattlegroupProblemKind.TooManyPoints, [], Count: points, Allowed: allowed));
        return problems;
    }

    /// <summary>
    /// The activation points the cards cost: in each type, the first card costs the division's first cost for that
    /// type, the second its second, and so on. Cards past the type's last cost are not counted (they are a problem).
    /// </summary>
    public static int Points(IReadOnlyList<BattlegroupCard> cards, Division division, Func<string, string?> typeOf)
    {
        int points = 0;
        foreach (var type in cards.GroupBy(card => typeOf(card.Unit) ?? ""))
            if (division.Costs.TryGetValue(type.Key, out IReadOnlyList<int>? costs))
                points += costs.Take(type.Count()).Sum();
        return points;
    }

    /// <summary>What is wrong with one card on its own: its unit, transport or veterancy.</summary>
    private static BattlegroupProblem? CardProblem(BattlegroupCard card, int place, Division division)
    {
        DivisionUnit? rule = division.Units.FirstOrDefault(unit => unit.Unit == card.Unit);
        if (rule is null) return new BattlegroupProblem(BattlegroupProblemKind.UnitNotInDivision, [place], card.Unit);
        if (card.Transport is null && !rule.AvailableWithoutTransport)
            return new BattlegroupProblem(BattlegroupProblemKind.NeedsTransport, [place], card.Unit);
        if (card.Transport is string transport && !rule.Transports.Contains(transport))
            return new BattlegroupProblem(BattlegroupProblemKind.TransportNotAllowed, [place], card.Unit, Transport: transport);
        bool offered = card.Veterancy >= 0 && card.Veterancy < rule.VeterancyMultipliers.Count && rule.VeterancyMultipliers[card.Veterancy] > 0;
        if (!offered) return new BattlegroupProblem(BattlegroupProblemKind.VeterancyNotOffered, [place], card.Unit, Count: card.Veterancy);
        return null;
    }

    /// <summary>Each unit with more cards than the division allows of it (its MaxPackNumber).</summary>
    private static IEnumerable<BattlegroupProblem> TooManyOfAUnit(IReadOnlyList<BattlegroupCard> cards, Division division)
    {
        foreach (var unit in Places(cards, card => card.Unit))
        {
            DivisionUnit? rule = division.Units.FirstOrDefault(each => each.Unit == unit.Key);
            if (rule is not null && unit.Value.Count > rule.Cards)
                yield return new BattlegroupProblem(BattlegroupProblemKind.TooManyCardsOfUnit, unit.Value, unit.Key, unit.Value.Count, rule.Cards);
        }
    }

    /// <summary>Each type with more cards than the division has costs for.</summary>
    private static IEnumerable<BattlegroupProblem> TooManyOfAType(IReadOnlyList<BattlegroupCard> cards, Division division, Func<string, string?> typeOf)
    {
        foreach (var type in Places(cards, card => typeOf(card.Unit) ?? ""))
        {
            if (type.Key == "") continue;
            int allowed = division.Costs.TryGetValue(type.Key, out IReadOnlyList<int>? costs) ? costs.Count : 0;
            if (type.Value.Count > allowed)
                yield return new BattlegroupProblem(BattlegroupProblemKind.TooManyCardsOfType, type.Value, type.Key, type.Value.Count, allowed);
        }
    }

    /// <summary>The places of the cards, by a key of each, keys in the order first met.</summary>
    private static List<KeyValuePair<string, List<int>>> Places(IReadOnlyList<BattlegroupCard> cards, Func<BattlegroupCard, string> keyOf)
    {
        var places = new List<KeyValuePair<string, List<int>>>();
        for (int place = 0; place < cards.Count; place++)
        {
            string key = keyOf(cards[place]);
            int found = places.FindIndex(each => each.Key == key);
            if (found < 0) places.Add(new(key, [place]));
            else places[found].Value.Add(place);
        }
        return places;
    }
}
