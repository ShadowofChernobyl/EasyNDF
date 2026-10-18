namespace EasyNdf.Data.Units;

/// <summary>
/// The Units tab's filters: the search text, the side, the nations ticked, the role, the division, "Only units in a
/// division" and the factory (the row of tabs above the list). A unit is shown when it passes all of them.
/// </summary>
public sealed class UnitFilter
{
    /// <summary>The filters <see cref="Matches"/> can leave out, to count what the list would show without them.</summary>
    public enum Ignoring
    {
        /// <summary>None: all the filters count.</summary>
        Nothing,
        /// <summary>The factory, to count the units each factory tab would show.</summary>
        Factory,
        /// <summary>The nations and the factory, to count each nation's units in the nation list.</summary>
        NationAndFactory,
    }

    /// <summary>Text the unit's name or descriptor must contain, in any capitals; empty for any.</summary>
    public string Text { get; set; } = "";

    /// <summary>The side, as units give it ("NATO", "Axis"...); empty for both.</summary>
    public string Side { get; set; } = "";

    /// <summary>The nations ticked; none ticked means every nation.</summary>
    public HashSet<string> Nations { get; } = new(StringComparer.Ordinal);

    /// <summary>The role; empty for any.</summary>
    public string Role { get; set; } = "";

    /// <summary>The division's name; empty for any.</summary>
    public string Division { get; set; } = "";

    /// <summary>Only units that are in at least one multiplayer division.</summary>
    public bool OnlyInDivision { get; set; } = true;

    /// <summary>The factory; empty for all of them (the ALL tab).</summary>
    public string Factory { get; set; } = "";

    /// <summary>Whether a unit passes the filters, leaving out the ones <paramref name="ignoring"/> names.</summary>
    public bool Matches(Unit unit, Ignoring ignoring = Ignoring.Nothing)
    {
        if (Side != "" && unit.Side != Side) return false;
        if (ignoring != Ignoring.NationAndFactory && Nations.Count > 0 && !Nations.Contains(unit.Nation)) return false;
        if (Role != "" && unit.Role != Role) return false;
        if (Division != "" && !unit.Divisions.Contains(Division)) return false;
        if (OnlyInDivision && unit.Divisions.Count == 0) return false;
        if (ignoring == Ignoring.Nothing && Factory != "" && unit.Factory != Factory) return false;
        if (Text != "" && !unit.Name.Contains(Text, StringComparison.CurrentCultureIgnoreCase)
            && !unit.Descriptor.Contains(Text, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>The units that pass, in their order.</summary>
    public List<Unit> Apply(IEnumerable<Unit> units) => units.Where(unit => Matches(unit)).ToList();

    /// <summary>How many units each factory tab would show (the other filters still count), by factory.</summary>
    public Dictionary<string, int> FactoryCounts(IEnumerable<Unit> units) =>
        units.Where(unit => Matches(unit, Ignoring.Factory)).GroupBy(unit => unit.Factory).ToDictionary(group => group.Key, group => group.Count());

    /// <summary>
    /// How many units each nation has on a side, with the other filters (but not the nations and the factory), by
    /// nation. A nation can fight on both sides (Romania in Steel Division 2), so each side is counted on its own.
    /// </summary>
    public Dictionary<string, int> NationCounts(IEnumerable<Unit> units, string side) =>
        units.Where(unit => unit.Side == side && Matches(unit, Ignoring.NationAndFactory)).GroupBy(unit => unit.Nation).ToDictionary(group => group.Key, group => group.Count());
}
