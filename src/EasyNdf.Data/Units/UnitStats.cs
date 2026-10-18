namespace EasyNdf.Data.Units;

/// <summary>
/// A number Compare shows for each unit: its key (for its label in the app's texts), how to read it, whether more is
/// better, and the value a full bar stands for.
/// </summary>
/// <param name="Key">The stat's name in the app's texts, such as "Cost".</param>
/// <param name="Read">How to read it from a unit; null when the unit has none.</param>
/// <param name="HigherIsBetter">False for cost, where less is better.</param>
/// <param name="FullBar">The value a full block bar stands for; bigger values fill it too.</param>
public sealed record UnitStat(string Key, Func<Unit, double?> Read, bool HigherIsBetter, double FullBar)
{
    /// <summary>The stats Compare lists, in order.</summary>
    public static IReadOnlyList<UnitStat> All { get; } =
    [
        new("Cost", unit => unit.Cost, false, 400),
        new("Strength", unit => unit.Strength, true, 20),
        new("FrontArmor", unit => unit.Armor.Front, true, 25),
        new("SideArmor", unit => unit.Armor.Sides, true, 25),
        new("RearArmor", unit => unit.Armor.Rear, true, 25),
        new("TopArmor", unit => unit.Armor.Top, true, 25),
        new("Speed", unit => unit.Speed, true, 120),
        new("RoadSpeed", unit => unit.RoadSpeed, true, 150),
        new("Optics", unit => unit.Optics, true, 4000),
        new("Stealth", unit => unit.Stealth, true, 3),
        new("LongestRange", unit => unit.LongestRange, true, 15000),
    ];

    /// <summary>
    /// The best value among some units: the highest, or for cost the lowest. Null when fewer than two of them have
    /// the stat, as there is nothing to be best against.
    /// </summary>
    public double? Best(IEnumerable<Unit> units)
    {
        List<double> values = units.Select(Read).OfType<double>().ToList();
        if (values.Count < 2) return null;
        return HigherIsBetter ? values.Max() : values.Min();
    }

    /// <summary>How many whole blocks of a bar of <paramref name="blocks"/> a value fills: at least one for any value above 0.</summary>
    public static int BlocksFor(double? value, double fullBar, int blocks)
    {
        if (value is not double v || v <= 0 || fullBar <= 0) return 0;
        return Math.Max(1, (int)Math.Round(Math.Min(1, v / fullBar) * blocks, MidpointRounding.AwayFromZero));
    }

    /// <summary>Whether a difference from the pinned unit is better (1), worse (-1) or the same (0).</summary>
    public int Feel(double difference) => difference == 0 ? 0 : (difference > 0) == HigherIsBetter ? 1 : -1;
}
