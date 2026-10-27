using EasyNdf.Data.Game;

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
    /// <param name="game">The game's divisions.</param>
    /// <param name="mod">The mod.</param>
    /// <param name="rosters">The units of generated divisions; without it, a generated division has only the units the user added.</param>
    public static List<Division> Apply(IReadOnlyList<Division> game, Mod mod, DivisionRosters? rosters = null)
    {
        Dictionary<string, Division> byDescriptor = game.GroupBy(division => division.Descriptor).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var divisions = new List<Division>();
        foreach (Division division in game.Where(division => division.IsForMultiplayer))
        {
            ModDivision? change = mod.Divisions.FirstOrDefault(each => !each.IsNew && each.Descriptor == division.Descriptor);
            divisions.Add(change is null ? division : change.Apply(division));
        }
        foreach (ModDivision added in mod.Divisions.Where(each => each.IsNew))
            if (byDescriptor.TryGetValue(added.StartsFrom, out Division? start)) divisions.Add(WithGeneratedBattlegroup(added.Apply(StartOf(added, start, rosters)), added, rosters));
        return divisions;
    }

    /// <summary>
    /// A new division with its Official Battlegroup as its Generation Settings make it from its units, when it has
    /// them (<see cref="ModDivision.BattlegroupGeneration"/>) and the units are known; as it is otherwise.
    /// </summary>
    public static Division WithGeneratedBattlegroup(Division division, ModDivision entry, DivisionRosters? rosters)
    {
        if (entry.BattlegroupGeneration is not BattlegroupGeneration settings || rosters is null) return division;
        List<GeneratedCard> generated = BattlegroupGenerator.Cards(division, rosters.UnitsByDescriptor, settings);
        return division with
        {
            Battlegroup = new Battlegroup
            {
                Descriptor = ModDivision.DeckDescriptorOf(entry.Descriptor),
                Name = entry.BattlegroupName,
                Cards = [.. generated.Select(card => card.Card)],
                Generated = generated,
            },
        };
    }

    /// <summary>
    /// Saves a generated division's Generation Settings: its battlegroup is made from them from now on, in place of
    /// any cards it had.
    /// </summary>
    public static void SetBattlegroupGeneration(Mod mod, string descriptor, BattlegroupGeneration settings)
    {
        if (EntryOf(mod, descriptor) is not { IsGenerated: true } entry) return;
        entry.BattlegroupGeneration = settings;
        entry.BattlegroupCards = null;
    }

    /// <summary>
    /// "Make Fixed" in the Generation Settings window: a generated battlegroup keeps the cards it has now, as an
    /// ordinary battlegroup changed card by card.
    /// </summary>
    public static void MakeBattlegroupFixed(Mod mod, IReadOnlyList<Division> game, string descriptor, DivisionRosters rosters)
    {
        if (EntryOf(mod, descriptor) is not { BattlegroupGeneration: not null } entry) return;
        Division shown = WithGeneratedBattlegroup(entry.Apply(StartOf(entry, game, rosters)), entry, rosters);
        entry.BattlegroupCards = [.. shown.Battlegroup?.Cards ?? []];
        entry.BattlegroupGeneration = null;
    }

    /// <summary>
    /// The division an entry's changes apply to: its starting division, with, for a generated division, the units its
    /// generation gives it in place of the starting division's (<see cref="DivisionRosters.StartOf"/>).
    /// </summary>
    private static Division StartOf(ModDivision entry, Division start, DivisionRosters? rosters) =>
        entry.IsGenerated ? rosters?.StartOf(entry, start) ?? start with { Units = [] } : start;

    /// <summary>The division an entry's changes apply to, found among the game's (<see cref="StartOf(ModDivision, Division, DivisionRosters?)"/>).</summary>
    private static Division StartOf(ModDivision entry, IReadOnlyList<Division> game, DivisionRosters? rosters) =>
        StartOf(entry, game.First(division => division.Descriptor == entry.StartsFrom), rosters);

    /// <summary>The mod's own entry for a division shown: the new division, or its change to the game's; null for none yet.</summary>
    public static ModDivision? EntryOf(Mod mod, string descriptor) =>
        mod.Divisions.FirstOrDefault(each => each.Descriptor == descriptor);

    /// <summary>
    /// Adds units to a division shown: to the mod's new division, or to its change to the game's, made when there is
    /// none yet. Each unit comes as another division of the game has it (<see cref="RuleFor"/>).
    /// </summary>
    /// <returns>How many units were added: those already in the division are not added again.</returns>
    public static int AddUnits(Mod mod, IReadOnlyList<Division> game, string descriptor, IEnumerable<string> units, DivisionRosters? rosters = null)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        Division start = StartOf(entry, game, rosters);
        int added = 0;
        foreach (string unit in units)
            if (entry.AddUnit(RuleFor(unit, game), start)) added++;
        return added;
    }

    /// <summary>
    /// Changes how a division shown has a unit (its cards, units per card or veterancy), in the mod's new division or in
    /// its change to the game's; a change that is left changing nothing is let go of.
    /// </summary>
    public static void SetRule(Mod mod, IReadOnlyList<Division> game, string descriptor, DivisionUnit rule, DivisionRosters? rosters = null)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        entry.SetRule(rule, StartOf(entry, game, rosters));
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>Takes a unit out of a division shown; a change that is left changing nothing is let go of.</summary>
    public static void RemoveUnit(Mod mod, IReadOnlyList<Division> game, string descriptor, string unit, DivisionRosters? rosters = null)
    {
        ModDivision entry = EntryOf(mod, descriptor) ?? AddChange(mod, descriptor);
        entry.RemoveUnit(unit, StartOf(entry, game, rosters));
        if (entry.ChangesNothing) mod.Divisions.Remove(entry);
    }

    /// <summary>
    /// A new division in the mod, named <paramref name="name"/>, with <paramref name="units"/>. Its nation and side are
    /// those given (else <paramref name="startsFrom"/>'s); everything else starts as <see cref="NewDivisionStart"/>
    /// sets it. <paramref name="startsFrom"/> is only the frame its files are made from: none of its values are kept.
    /// </summary>
    /// <returns>The new division's descriptor.</returns>
    public static string AddNew(Mod mod, IReadOnlyList<Division> game, string name, Division startsFrom, IEnumerable<string> units, GameTitle title,
        (string Country, string Side)? nation = null)
    {
        HashSet<string> taken = [.. game.Select(division => division.Descriptor), .. mod.Divisions.Select(division => division.Descriptor)];
        var created = new ModDivision
        {
            Descriptor = ModDivision.NewDescriptor(name, taken),
            IsNew = true,
            StartsFrom = startsFrom.Descriptor,
            Name = name.Trim(),
            KeepsUnits = false,
            // never empty, so the game always finds a summary; the history is the user's to write
            Summary = name.Trim(),
            History = "",
        };
        NewDivisionStart.Give(created, game, mod, title);
        if (nation is (string country, string side)) (created.Country, created.Side) = (country, side);
        mod.Divisions.Add(created);
        AddUnits(mod, game, created.Descriptor, units);
        return created.Descriptor;
    }

    /// <summary>
    /// A generated division in the mod (<see cref="DivisionGeneration"/>), named <paramref name="name"/>: no units of its
    /// own, as they are made each time; its points and costs as its generation gives them; its battlegroup generated
    /// with the ready setting Balanced; everything else as a new division starts (<see cref="NewDivisionStart"/>).
    /// </summary>
    /// <param name="startsFrom">The frame its files are made from: a game division (none of its values are kept).</param>
    /// <param name="nation">Its nation (<see cref="DivisionRosters.NationOf"/>) and side.</param>
    /// <returns>The new division's descriptor.</returns>
    public static string AddGenerated(Mod mod, IReadOnlyList<Division> game, string name, Division startsFrom, DivisionGeneration generation,
        (string Country, string Side) nation, GameTitle title)
    {
        string descriptor = AddNew(mod, game, name, startsFrom, [], title, nation);
        ModDivision created = EntryOf(mod, descriptor)!;
        created.Generation = generation;
        // its units are those generated, the user's own changes on top
        created.KeepsUnits = true;
        created.ActivationPoints = generation.ActivationPoints;
        created.Costs = generation.Costs();
        // its Official Battlegroup is made from its units too, as its Generation Settings choose
        created.BattlegroupGeneration = new BattlegroupGeneration();
        return descriptor;
    }

    /// <summary>
    /// "Change…": gives a generated division another way of being made. Its points and costs follow; the user's own
    /// changes to its units are kept.
    /// </summary>
    public static void ChangeGeneration(Mod mod, string descriptor, DivisionGeneration generation, (string Country, string Side) nation)
    {
        if (EntryOf(mod, descriptor) is not { IsGenerated: true } entry) return;
        entry.Generation = generation;
        (entry.Country, entry.Side) = nation;
        entry.ActivationPoints = generation.ActivationPoints;
        entry.Costs = generation.Costs();
    }

    /// <summary>"Make Fixed": a generated division keeps the units it has now, as an ordinary new division (<see cref="ModDivision.MakeFixed"/>).</summary>
    public static void MakeFixed(Mod mod, IReadOnlyList<Division> game, string descriptor, DivisionRosters rosters)
    {
        if (EntryOf(mod, descriptor) is not { IsGenerated: true } entry) return;
        // its battlegroup is made from its units: fixed with them, as it is now
        if (entry.BattlegroupGeneration is not null) MakeBattlegroupFixed(mod, game, descriptor, rosters);
        entry.MakeFixed(StartOf(entry, game, rosters));
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
        // a deck number of its own, as two divisions cannot share one in deck codes
        copy.DeckNumber = NewDivisionStart.NextDeckNumber(game, mod);
        copy.Name = name.Trim();
        // a copy of a game division keeps its texts, as its other details
        Division? start = game.FirstOrDefault(division => division.Descriptor == descriptor);
        copy.Summary ??= start?.Summary ?? "";
        copy.History ??= start?.History ?? "";
        // the same battlegroup, under a name made from its own (the game's name is the other division's)
        copy.BattlegroupCards ??= start?.Battlegroup?.Cards.ToList();
        copy.BattlegroupName = "";
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
