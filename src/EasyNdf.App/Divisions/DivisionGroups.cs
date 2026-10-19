using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>A unit of a division: how the division has it (its rule), and the unit itself.</summary>
/// <param name="Rule">How many cards, how many units in each, which transports.</param>
/// <param name="Unit">The unit, as the Units tab read it.</param>
public sealed record DivisionEntry(DivisionUnit Rule, Unit Unit);

/// <summary>The units of one category (factory) of a division, with that category's activation-point costs.</summary>
/// <param name="Factory">The category's code: "Tanks".</param>
/// <param name="Costs">The cost of each card of the category in turn; empty when the division has none for it.</param>
/// <param name="Entries">The division's units of that category, in the order of its rule.</param>
public sealed record DivisionGroup(string Factory, IReadOnlyList<int> Costs, IReadOnlyList<DivisionEntry> Entries);

/// <summary>Sorts a division's units into its categories, as both of the Divisions tab's views show them.</summary>
public static class DivisionGroups
{
    /// <summary>
    /// The division's units by category, in the game's order of categories (those the app does not know last). A unit
    /// the source's units do not have is left out. <paramref name="factory"/> keeps one category only; empty keeps all.
    /// </summary>
    public static List<DivisionGroup> Of(Division division, IReadOnlyDictionary<string, Unit> units, GameTitle game, string factory)
    {
        List<DivisionEntry> entries = Entries(division, units);
        IReadOnlyList<string> order = UnitNames.FactoriesOf(game);
        var groups = new List<DivisionGroup>();
        IEnumerable<IGrouping<string, DivisionEntry>> byFactory = entries
            .GroupBy(entry => entry.Unit.Factory)
            .OrderBy(group => OrderOf(order, group.Key));
        foreach (IGrouping<string, DivisionEntry> group in byFactory)
        {
            if (factory != "" && group.Key != factory) continue;
            groups.Add(new DivisionGroup(group.Key, division.Costs.GetValueOrDefault(group.Key) ?? [], [.. group]));
        }
        return groups;
    }

    /// <summary>The division's units that the source's units have, in the order of its rule.</summary>
    public static List<DivisionEntry> Entries(Division division, IReadOnlyDictionary<string, Unit> units)
    {
        var entries = new List<DivisionEntry>();
        foreach (DivisionUnit rule in division.Units)
            if (units.TryGetValue(rule.Unit, out Unit? unit)) entries.Add(new DivisionEntry(rule, unit));
        return entries;
    }

    /// <summary>How many of the division's units in each category, for the category tabs.</summary>
    public static Dictionary<string, int> CountsByFactory(Division division, IReadOnlyDictionary<string, Unit> units) =>
        Entries(division, units).GroupBy(entry => entry.Unit.Factory).ToDictionary(group => group.Key, group => group.Count());

    /// <summary>A category's place in the game's order; one the app does not know goes after them all.</summary>
    private static int OrderOf(IReadOnlyList<string> order, string factory)
    {
        int place = order.ToList().IndexOf(factory);
        return place < 0 ? order.Count : place;
    }
}
