namespace EasyNdf.Data.Divisions;

/// <summary>What is wrong between a division's units and its cost matrix (<see cref="TabRows"/>).</summary>
public enum TabRowFault
{
    /// <summary>A division offers a unit of a tab its cost matrix has no row for: the game crashes when it generates the mod.</summary>
    NoRow,

    /// <summary>A ready-made deck holds more packs of a tab than the division's row has slots: the game stops with "Error in deck autofill".</summary>
    TooFewSlots,
}

/// <summary>One fault found by <see cref="TabRows"/>.</summary>
/// <param name="Division">The division's descriptor, such as Descriptor_Deck_Division_BEL_16e_Mecanisee_multi.</param>
/// <param name="Tab">The tab as the files name it, such as Factory/Support.</param>
/// <param name="Count">How many of the division's units are in the tab (no row), or how many packs of it the deck holds (too few slots).</param>
/// <param name="Slots">How many slots the row has; 0 for no row.</param>
public sealed record TabRowProblem(TabRowFault Fault, string Division, string Tab, int Count, int Slots);

/// <summary>
/// WARNO's check of a mod that moves units between the Armory's tabs (a unit's FactoryType), as the game makes it when
/// it generates the mod: every tab a division's units are in needs a row in the division's cost matrix
/// (DivisionCostMatrix.ndf), and that row needs a slot for each pack of the tab in the division's ready-made deck
/// (Decks.ndf). Without the row the game crashes with no message; with too few slots it stops with "Error in deck
/// autofill ... packs exceed deck maximum activation points". The game's own files have neither fault.
/// </summary>
public static class TabRows
{
    /// <summary>The files the check reads, by name: all six must be at hand.</summary>
    public static IReadOnlyList<string> Files { get; } = ["DivisionCostMatrix.ndf", "Divisions.ndf", "DivisionRules.ndf", "DeckPacks.ndf", "Decks.ndf"];

    /// <summary>Finds the faults in the six files' texts, as the mods' rules leave them.</summary>
    /// <param name="units">The units' file (TacticalUnits.ndf, or UniteDescriptor.ndf before WARNO v210942).</param>
    public static List<TabRowProblem> Find(string units, string costMatrix, string divisions, string divisionRules, string deckPacks, string decks)
    {
        // each unit's tab
        var tabOf = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        foreach (string line in Lines(units))
        {
            if (line.StartsWith("export ", StringComparison.Ordinal)) { current = WordAfter(line, "export "); continue; }
            if (current is not null && !tabOf.ContainsKey(current) && ValueOf(line, "FactoryType") is string tab) tabOf[current] = tab;
        }

        // each matrix's rows: the tab and how many slots it has
        var rowsOf = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        Dictionary<string, int>? rows = null;
        foreach (string line in Lines(costMatrix))
        {
            if (line.StartsWith("MatrixCostName_", StringComparison.Ordinal)) { rowsOf[WordAfter(line, "")] = rows = new(StringComparer.Ordinal); continue; }
            if (rows is null) continue;
            string trimmed = line.Trim();
            if (trimmed.StartsWith(']')) { rows = null; continue; }
            int open = trimmed.IndexOf('('), comma = trimmed.IndexOf(','), list = trimmed.IndexOf('['), listEnd = trimmed.IndexOf(']');
            if (open != 0 || comma < 0 || list < comma || listEnd < list) continue;
            rows[trimmed[1..comma].Trim()] = trimmed[(list + 1)..listEnd].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        }

        // each division's matrix
        var matrixOf = new Dictionary<string, string>(StringComparer.Ordinal);
        current = null;
        foreach (string line in Lines(divisions))
        {
            if (line.StartsWith("export ", StringComparison.Ordinal)) { current = WordAfter(line, "export "); continue; }
            if (current is not null && ValueOf(line, "CostMatrix") is string matrix) matrixOf[current] = matrix;
        }

        var problems = new List<TabRowProblem>();
        Dictionary<string, int>? RowsOf(string division) =>
            matrixOf.TryGetValue(division, out string? matrix) && rowsOf.TryGetValue(matrix, out Dictionary<string, int>? its) ? its : null;

        // the units each division offers: a tab with no row
        var unitsWithoutRow = new Dictionary<(string Division, string Tab), HashSet<string>>();
        current = null;
        foreach (string line in Lines(divisionRules))
        {
            if (line.StartsWith("Descriptor_Deck_Division_", StringComparison.Ordinal))
            {
                string name = WordAfter(line, "");
                current = name.EndsWith("_Rule", StringComparison.Ordinal) ? name[..^"_Rule".Length] : name;
                continue;
            }
            if (current is null || ValueOf(line, "UnitDescriptor") is not string unit) continue;
            if (RowsOf(current) is not { } its || !tabOf.TryGetValue(LastPart(unit), out string? tab) || its.ContainsKey(tab)) continue;
            if (!unitsWithoutRow.TryGetValue((current, tab), out HashSet<string>? found)) unitsWithoutRow[(current, tab)] = found = new(StringComparer.Ordinal);
            found.Add(unit);
        }
        foreach (var (key, found) in unitsWithoutRow) problems.Add(new TabRowProblem(TabRowFault.NoRow, key.Division, key.Tab, found.Count, 0));

        // each pack's unit, then each ready-made deck's packs by tab: more than the row has slots
        var unitOfPack = new Dictionary<string, string>(StringComparer.Ordinal);
        current = null;
        foreach (string line in Lines(deckPacks))
        {
            if (line.StartsWith("Descriptor_Deck_Pack_", StringComparison.Ordinal) || line.StartsWith("export Descriptor_Deck_Pack_", StringComparison.Ordinal))
            {
                current = WordAfter(line, line.StartsWith("export ", StringComparison.Ordinal) ? "export " : "");
                continue;
            }
            if (current is not null && !unitOfPack.ContainsKey(current) && ValueOf(line, "Unit") is string unit) unitOfPack[current] = LastPart(unit);
        }
        string? deckDivision = null;
        var packsByTab = new Dictionary<string, int>(StringComparer.Ordinal);
        void EndDeck()
        {
            if (deckDivision is not null && RowsOf(deckDivision) is { } its)
                foreach (var (tab, count) in packsByTab)
                    if (its.TryGetValue(tab, out int slots) && count > slots)
                        problems.Add(new TabRowProblem(TabRowFault.TooFewSlots, deckDivision, tab, count, slots));
            deckDivision = null;
            packsByTab.Clear();
        }
        foreach (string line in Lines(decks))
        {
            if (line.StartsWith("export ", StringComparison.Ordinal)) { EndDeck(); continue; }
            if (ValueOf(line, "DeckDivision") is string division) { deckDivision = LastPart(division); continue; }
            int pack = line.IndexOf("~/Descriptor_Deck_Pack_", StringComparison.Ordinal);
            if (pack < 0) continue;
            string name = WordAfter(line[(pack + 2)..], "");
            if (unitOfPack.TryGetValue(name, out string? unit) && tabOf.TryGetValue(unit, out string? tab)) packsByTab[tab] = packsByTab.GetValueOrDefault(tab) + 1;
        }
        EndDeck();
        return problems;
    }

    /// <summary>A text's lines, each without the comments a rule's edit leaves on it ("/*Inserted ...*/").</summary>
    private static IEnumerable<string> Lines(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            for (int open = line.IndexOf("/*", StringComparison.Ordinal); open >= 0; open = line.IndexOf("/*", StringComparison.Ordinal))
            {
                int close = line.IndexOf("*/", open + 2, StringComparison.Ordinal);
                line = close < 0 ? line[..open] : line[..open] + line[(close + 2)..];
            }
            yield return line;
        }
    }

    /// <summary>The name that follows a line's start: letters, digits and underscores.</summary>
    private static string WordAfter(string line, string start)
    {
        int from = start.Length, to = from;
        while (to < line.Length && (char.IsLetterOrDigit(line[to]) || line[to] == '_')) to++;
        return line[from..to];
    }

    /// <summary>The value a line gives a property ("Name = value"), without a comma after it; null when the line is not that property's.</summary>
    private static string? ValueOf(string line, string property)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith(property, StringComparison.Ordinal)) return null;
        string rest = trimmed[property.Length..].TrimStart();
        if (!rest.StartsWith('=')) return null;
        return rest[1..].Trim().TrimEnd(',').Trim();
    }

    /// <summary>The name at the end of a reference: Descriptor_Unit_X of $/GFX/Unit/Descriptor_Unit_X.</summary>
    private static string LastPart(string reference) => reference[(reference.LastIndexOf('/') + 1)..];
}
