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
/// <param name="Entries">The division's units of that category, in the order the game shows them (<see cref="UnitGameOrder"/>).</param>
public sealed record DivisionGroup(string Factory, IReadOnlyList<int> Costs, IReadOnlyList<DivisionEntry> Entries);

/// <summary>Sorts a division's units into its categories, as both of the Divisions tab's views show them.</summary>
public static class DivisionGroups
{
    /// <summary>
    /// The division's units by category, in the game's order of categories (those the app does not know last). A unit
    /// the source's units do not have is left out. <paramref name="factory"/> keeps one category only; empty keeps all.
    /// With <paramref name="withEmpty"/>, a category with none of the units shown is kept too, so its costs can be seen
    /// and changed: each of the game's categories, and any other the division has cards for. One that is not the game's
    /// and has no cards is left out (WARNO's cost lists name Steel Division 2's Defenses, which WARNO has no units of).
    /// Each category's units are in the order the game's armory shows them.
    /// </summary>
    /// <param name="allUnits">Every unit of the source by descriptor, for the game's order, which follows chains of upgrades through units the division does not have.</param>
    public static List<DivisionGroup> Of(Division division, IReadOnlyDictionary<string, Unit> units, GameTitle game, string factory,
        IReadOnlyDictionary<string, Unit> allUnits, bool withEmpty = false)
    {
        IReadOnlyList<string> order = UnitNames.FactoriesOf(game);
        Dictionary<string, List<DivisionEntry>> byFactory = Entries(division, units)
            .GroupBy(entry => entry.Unit.Factory)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        IEnumerable<string> categories = byFactory.Keys;
        if (withEmpty)
            categories = categories.Concat(order).Concat(division.Costs.Where(cost => cost.Value.Count > 0).Select(cost => cost.Key));
        var groups = new List<DivisionGroup>();
        foreach (string category in categories.Distinct().OrderBy(category => OrderOf(order, category)))
        {
            if (factory != "" && category != factory) continue;
            List<DivisionEntry> entries = UnitGameOrder.Sort(byFactory.GetValueOrDefault(category) ?? [], entry => entry.Unit, allUnits);
            groups.Add(new DivisionGroup(category, division.Costs.GetValueOrDefault(category) ?? [], entries));
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
