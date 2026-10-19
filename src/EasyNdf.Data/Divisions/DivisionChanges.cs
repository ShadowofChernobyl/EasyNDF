namespace EasyNdf.Data.Divisions;

/// <summary>
/// What a mod's divisions do to the game's: the divisions as the game will have them, and adding units to them or
/// taking units out. The mod keeps only its changes (<see cref="ModDivision"/>).
/// </summary>
public static class DivisionChanges
{
    /// <summary>
    /// The game's multiplayer divisions with the mod's changes made, then the mod's new divisions. A new division whose
    /// starting division the source does not have is left out.
    /// </summary>
    public static List<Division> Apply(IReadOnlyList<Division> game, Mod mod)
    {
        Dictionary<string, Division> byDescriptor = game.GroupBy(division => division.Descriptor).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var divisions = new List<Division>();
        foreach (Division division in game.Where(division => division.IsForMultiplayer))
        {
            ModDivision? change = mod.Divisions.FirstOrDefault(each => !each.IsNew && each.Descriptor == division.Descriptor);
            divisions.Add(change is null ? division : change.Apply(division));
        }
        foreach (ModDivision added in mod.Divisions.Where(each => each.IsNew))
            if (byDescriptor.TryGetValue(added.StartsFrom, out Division? start)) divisions.Add(added.Apply(start));
        return divisions;
    }

    /// <summary>The mod's own entry for a division shown: the new division, or its change to the game's; null for none yet.</summary>
    public static ModDivision? EntryOf(Mod mod, string descriptor) =>
        mod.Divisions.FirstOrDefault(each => each.Descriptor == descriptor);

    /// <summary>
    /// Adds units to a division shown: to the mod's new division, or to its change to the game's, made when there is
    /// none yet. Each unit comes as another division of the game has it (<see cref="RuleFor"/>).
    /// </summary>
    /// <returns>How many units were added: those already in the division are not added again.</returns>
    public static int AddUnits(Mod mod, IReadOnlyList<Division> game, string descriptor, IEnumerable<string> units)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        Division start = game.First(division => division.Descriptor == entry.StartsFrom);
        int added = 0;
        foreach (string unit in units)
            if (entry.AddUnit(RuleFor(unit, game), start)) added++;
        return added;
    }

    /// <summary>
    /// Changes how a division shown has a unit (its cards, units per card or veterancy), in the mod's new division or in
    /// its change to the game's; a change that is left changing nothing is let go of.
    /// </summary>
    public static void SetRule(Mod mod, IReadOnlyList<Division> game, string descriptor, DivisionUnit rule)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        entry.SetRule(rule, game.First(division => division.Descriptor == entry.StartsFrom));
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>Takes a unit out of a division shown; a change that is left changing nothing is let go of.</summary>
    public static void RemoveUnit(Mod mod, IReadOnlyList<Division> game, string descriptor, string unit)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        entry.RemoveUnit(unit, game.First(division => division.Descriptor == entry.StartsFrom));
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>
    /// A new division in the mod, named <paramref name="name"/>, that starts as a copy of a game division's details
    /// without its units, and has <paramref name="units"/>.
    /// </summary>
    /// <returns>The new division's descriptor.</returns>
    public static string AddNew(Mod mod, IReadOnlyList<Division> game, string name, Division startsFrom, IEnumerable<string> units)
    {
        HashSet<string> taken = [.. game.Select(division => division.Descriptor), .. mod.Divisions.Select(division => division.Descriptor)];
        var created = new ModDivision
        {
            Descriptor = ModDivision.NewDescriptor(name, taken),
            IsNew = true,
            StartsFrom = startsFrom.Descriptor,
            Name = name.Trim(),
            KeepsUnits = false,
        };
        mod.Divisions.Add(created);
        AddUnits(mod, game, created.Descriptor, units);
        return created.Descriptor;
    }

    /// <summary>
    /// The division of the game a new division for a unit starts from: the first multiplayer division that has the
    /// unit, else the first of its nation, else the first of its side; null when the game has none.
    /// </summary>
    public static Division? StartFor(IReadOnlyList<Division> game, string unit, string nation, string side)
    {
        IEnumerable<Division> multiplayer = game.Where(division => division.IsForMultiplayer);
        return multiplayer.FirstOrDefault(division => division.Units.Any(each => each.Unit == unit))
            ?? multiplayer.FirstOrDefault(division => division.Country == nation && nation != "")
            ?? multiplayer.FirstOrDefault(division => division.Side == side)
            ?? multiplayer.FirstOrDefault();
    }

    /// <summary>
    /// How a division is to have a unit added to it: as the first of the game's multiplayer divisions with the unit has
    /// it (cards, units per card, veterancy, transports); for a unit in none, one card of one unit, at the lowest
    /// veterancy only, needing no transport.
    /// </summary>
    public static DivisionUnit RuleFor(string unit, IReadOnlyList<Division> game)
    {
        DivisionUnit? found = game.Where(division => division.IsForMultiplayer)
            .SelectMany(division => division.Units)
            .FirstOrDefault(each => each.Unit == unit);
        return found ?? new DivisionUnit { Unit = unit, Cards = 1, UnitsPerCard = 1, VeterancyMultipliers = [1.0, 0, 0, 0], AvailableWithoutTransport = true };
    }

    /// <summary>
    /// Renames a division shown: the mod's new division, or a game division (kept as a change; the game's own name
    /// given again lets the change go when it changes nothing else).
    /// </summary>
    public static void Rename(Mod mod, IReadOnlyList<Division> game, string descriptor, string name)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        string gameName = game.FirstOrDefault(division => division.Descriptor == descriptor)?.Name ?? "";
        entry.Name = !entry.IsNew && name == gameName ? "" : name.Trim();
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>
    /// A new division in the mod that is a copy of a division shown, named <paramref name="name"/>: the same details,
    /// the same units, the same changes the mod makes to them.
    /// </summary>
    /// <returns>The copy's descriptor.</returns>
    public static string Duplicate(Mod mod, IReadOnlyList<Division> game, string descriptor, string name)
    {
        ModDivision? entry = EntryOf(mod, descriptor);
        HashSet<string> taken = [.. game.Select(division => division.Descriptor), .. mod.Divisions.Select(division => division.Descriptor)];
        ModDivision copy = entry?.Copy() ?? new ModDivision { StartsFrom = descriptor };
        copy.Descriptor = ModDivision.NewDescriptor(name, taken);
        copy.IsNew = true;
        copy.IsRemoved = false;
        copy.Name = name.Trim();
        mod.Divisions.Add(copy);
        return copy.Descriptor;
    }

    /// <summary>
    /// Deletes a division shown: the mod's new division goes from the mod; a game division is taken out of the game's
    /// list (kept as a change, so it can be put back).
    /// </summary>
    public static void Delete(Mod mod, string descriptor)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        if (entry.IsNew) mod.Divisions.Remove(entry);
        else entry.IsRemoved = true;
    }

    /// <summary>Puts a game division the mod took out back in the game's list.</summary>
    public static void Restore(Mod mod, string descriptor)
    {
        if (EntryOf(mod, descriptor) is not ModDivision entry) return;
        entry.IsRemoved = false;
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>Whether the mod takes a game division out of the game's list.</summary>
    public static bool IsRemoved(Mod mod, string descriptor) => EntryOf(mod, descriptor) is { IsRemoved: true };

    /// <summary>
    /// Changes a division shown's details (its nation, type, points, place in the list, emblem, standout units or
    /// costs), in the mod's new division or in its change to the game's. Details set back as the starting division has
    /// them are let go of, and so is a change left changing nothing.
    /// </summary>
    public static void ChangeDetails(Mod mod, IReadOnlyList<Division> game, string descriptor, Action<ModDivision> change)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        change(entry);
        entry.ForgetUnchangedDetails(game.First(division => division.Descriptor == entry.StartsFrom));
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>The mod's change to a game division, made empty and added to the mod.</summary>
    private static ModDivision AddChange(Mod mod, string descriptor)
    {
        var change = new ModDivision { Descriptor = descriptor, StartsFrom = descriptor };
        mod.Divisions.Add(change);
        return change;
    }
}
