namespace EasyNdf.Data.Units;

/// <summary>
/// A division's units in the order the game's armory and deck builder show them, worked out from the game's own screens
/// (the US 3rd Armored's infantry and tanks): each chain of upgrades is one row there, and the rows and the units in each
/// follow from the units' costs. The game's list of a division's units (DivisionRules.ndf) does not set it: reversed in
/// a test, the game showed the same order.
/// <list type="bullet">
/// <item>A chain is the units that lead back, through UpgradeFromUnit, to the same first unit, even one the division
/// does not have: the M60 and the M2HB machine guns both upgrade from the National Guard's M60, so they share a row.</item>
/// <item>The rows go from the one with the cheapest unit to the dearest; on a tie, by that unit's name.</item>
/// <item>In a row, the chain's first unit comes first when the division has it (a leader, a command tank), then the
/// others from the cheapest, then by name.</item>
/// </list>
/// </summary>
public static class UnitGameOrder
{
    /// <summary>The most steps followed back along a chain, so a chain that leads back to itself still ends.</summary>
    private const int LongestChain = 50;

    /// <summary>Some of a division's units (one category's), in the game's order.</summary>
    /// <param name="items">What is sorted: a division's units, or lines of it.</param>
    /// <param name="unitOf">The unit of each.</param>
    /// <param name="allUnits">Every unit of the source by descriptor, to follow chains through units the division lacks.</param>
    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, Unit> unitOf, IReadOnlyDictionary<string, Unit> allUnits)
    {
        var rows = items.GroupBy(item => FirstOfChain(unitOf(item), allUnits))
            .Select(row => SortRow(row.Key, [.. row], unitOf))
            .OrderBy(row => CostOf(unitOf(Cheapest(row, unitOf))))
            .ThenBy(row => unitOf(Cheapest(row, unitOf)).Name, StringComparer.Ordinal);
        return [.. rows.SelectMany(row => row)];
    }

    /// <summary>The first unit of a unit's chain of upgrades, by descriptor: the one it leads back to.</summary>
    public static string FirstOfChain(Unit unit, IReadOnlyDictionary<string, Unit> allUnits)
    {
        string first = unit.Descriptor;
        string? before = unit.UpgradesFrom;
        for (int step = 0; step < LongestChain && !string.IsNullOrEmpty(before); step++)
        {
            first = before;
            before = allUnits.TryGetValue(before, out Unit? earlier) ? earlier.UpgradesFrom : null;
        }
        return first;
    }

    /// <summary>A row: the chain's first unit first, when the division has it, then the others from the cheapest, then by name.</summary>
    private static List<T> SortRow<T>(string firstOfChain, List<T> row, Func<T, Unit> unitOf) =>
        [.. row.OrderBy(item => unitOf(item).Descriptor == firstOfChain ? 0 : 1)
            .ThenBy(item => CostOf(unitOf(item)))
            .ThenBy(item => unitOf(item).Name, StringComparer.Ordinal)];

    /// <summary>A row's cheapest unit; on a tie, the first by name.</summary>
    private static T Cheapest<T>(List<T> row, Func<T, Unit> unitOf) =>
        row.OrderBy(item => CostOf(unitOf(item))).ThenBy(item => unitOf(item).Name, StringComparer.Ordinal).First();

    /// <summary>A unit's cost; one with none goes after the others.</summary>
    private static double CostOf(Unit unit) => unit.Cost ?? double.MaxValue;
}
