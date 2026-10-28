using System.Globalization;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The units of generated divisions (<see cref="DivisionGeneration"/>), made from a game's units and divisions: the
/// Armory makes them from the units it shows, and an export from the files on the Files tab. Each generation's units
/// are made once.
/// </summary>
/// <param name="units">The units to choose from, in the game's order.</param>
/// <param name="game">The game's divisions, for the numbers they give each unit and the nations they are for.</param>
/// <param name="packsInFile">The battlegroup cards DeckPacks.ndf has, when it was read; none otherwise.</param>
public sealed class DivisionRosters(IReadOnlyList<Unit> units, IReadOnlyList<Division> game, IReadOnlySet<string>? packsInFile = null)
{
    /// <summary>The battlegroup cards DeckPacks.ndf has, by descriptor, so generated battlegroups add only the others.</summary>
    public IReadOnlySet<string> PacksInFile { get; } = packsInFile ?? new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The numbers a unit no division has, and no similar unit either, is given.</summary>
    private static readonly DivisionUnit Fallback = new() { Unit = "", Cards = 10, UnitsPerCard = 10, VeterancyMultipliers = [1, 0.6, 0.4, 0.1] };

    /// <summary>How many similar units a unit no division has is compared with, the closest in cost first.</summary>
    private const int SimilarUnitsCompared = 5;

    /// <summary>The veterancy levels a card has, from the lowest.</summary>
    private const int VeterancyLevels = 4;

    /// <summary>Names in the order a person reads them, as the units of a generated division are listed.</summary>
    private static readonly CompareInfo NameOrder = CultureInfo.InvariantCulture.CompareInfo;

    private readonly Dictionary<string, IReadOnlyList<DivisionUnit>> made = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, DivisionUnit>> gameRules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DivisionUnit> similarRules = new(StringComparer.Ordinal);
    // the units with a game rule that gives units, for each set of game rules (see UnitsWithRules)
    private readonly Dictionary<Dictionary<string, DivisionUnit>, List<Unit>> unitsWithRules = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The rosters an export makes generated divisions from: the units and divisions of the files given, as they are on
    /// the Files tab (the game's or a mod's, after the mods' rules), with no game folder behind them.
    /// </summary>
    /// <param name="files">
    /// The files, such as UniteDescriptor.ndf, BuildingDescriptors.ndf, DeckSerializer.ndf, Divisions.ndf and
    /// DivisionRules.ndf; with WeaponDescriptor.ndf and the ammunition files the units have their weapons, and with
    /// DeckPacks.ndf its cards are known (<see cref="PacksInFile"/>).
    /// </param>
    /// <param name="title">The game they are of.</param>
    public static DivisionRosters FromFiles(IReadOnlyList<ChangedFile> files, Game.GameTitle title)
    {
        using var gameData = new GameDataFiles(files);
        UnitCatalog catalog = UnitReader.Read(gameData, title, new Game.GameTexts(), knownFlags: new Dictionary<string, string>());
        return new DivisionRosters(catalog.Units, catalog.Divisions, catalog.BattlegroupCards);
    }

    /// <summary>The units to choose from.</summary>
    public IReadOnlyList<Unit> AllUnits => units;

    private Dictionary<string, Unit>? unitsByDescriptor;

    /// <summary>The units to choose from, by descriptor (the first of each, should two share one).</summary>
    public IReadOnlyDictionary<string, Unit> UnitsByDescriptor =>
        unitsByDescriptor ??= units.GroupBy(unit => unit.Descriptor).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    /// <summary>
    /// A division's start with the units its generation gives it, for a generated division; the start as it is for any
    /// other. The division's own changes (<see cref="ModDivision.Added"/>, <see cref="ModDivision.Removed"/>) then
    /// apply on top, as they do to a game division's units.
    /// </summary>
    public Division StartOf(ModDivision change, Division start) =>
        change.Generation is DivisionGeneration generation ? start with { Units = UnitsFor(generation) } : start;

    /// <summary>The units a generation gives a division, with their cards, numbers and transports, A to Z.</summary>
    public IReadOnlyList<DivisionUnit> UnitsFor(DivisionGeneration generation)
    {
        string key = generation.ToJson().ToJsonString();
        if (made.TryGetValue(key, out IReadOnlyList<DivisionUnit>? known)) return known;
        List<Unit> taken = UnitsTaken(generation);
        List<Unit> transports = generation.AllTransports ? TransportsInOrder(taken) : [];
        List<DivisionUnit> rules = [.. taken.Select(unit => RuleFor(unit, generation, transports))];
        made[key] = rules;
        return rules;
    }

    // ---- which units ----

    /// <summary>
    /// The units a generation takes, A to Z by descriptor: those a deck can hold (with a category, a nation and a
    /// side) of its nation, of its side's nations, or of both sides; and the units of the nations that go into every
    /// division.
    /// </summary>
    public List<Unit> UnitsTaken(DivisionGeneration generation)
    {
        HashSet<string> everywhere = [.. generation.EveryDivisionNations];
        return [.. units.Where(IsDeckable)
            .Where(unit => everywhere.Contains(unit.Nation) || IsInScope(unit, generation))
            .OrderBy(unit => unit.Descriptor, NameComparer.Instance)];
    }

    /// <summary>Whether a unit is of the nation, side or sides a generation takes.</summary>
    private static bool IsInScope(Unit unit, DivisionGeneration generation) => generation.Units switch
    {
        GeneratedUnits.Nation => unit.Nation == generation.Nation,
        GeneratedUnits.Side => unit.Side == generation.Side,
        _ => true,
    };

    /// <summary>
    /// The nation a generated division is of: its own nation; for a side's or both sides' division, the nation of that
    /// side with the most units (WARNO's own lists each division under one nation).
    /// </summary>
    public string NationOf(DivisionGeneration generation)
    {
        if (generation.Units == GeneratedUnits.Nation) return generation.Nation;
        HashSet<string> everywhere = [.. generation.EveryDivisionNations];
        return units.Where(IsDeckable).Where(unit => unit.Side == generation.Side && !everywhere.Contains(unit.Nation))
            .GroupBy(unit => unit.Nation).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Whether a deck can hold a unit: it has a category, a nation and a side. (A unit with no deck-code number yet is
    /// given one by the export.)
    /// </summary>
    public static bool IsDeckable(Unit unit) => unit.Factory != "" && unit.Nation != "" && unit.Side != "";

    // ---- each unit's rule ----

    /// <summary>How the division has one unit: its cards and numbers for the generation, and its transports.</summary>
    /// <param name="transports">The division's units that carry others, in the order transports are listed (<see cref="TransportsInOrder"/>).</param>
    private DivisionUnit RuleFor(Unit unit, DivisionGeneration generation, List<Unit> transports)
    {
        (int perCard, IReadOnlyList<double> shares) = generation.Numbers switch
        {
            GeneratedNumbers.Unlimited => (DivisionGeneration.UnlimitedCount, (IReadOnlyList<double>)[1, 1, 1, 1]),
            GeneratedNumbers.Same => SameNumbers(generation.Counts),
            _ => GameNumbers(unit, generation),
        };
        return new DivisionUnit
        {
            Unit = unit.Descriptor,
            Cards = generation.Slots,
            UnitsPerCard = perCard,
            VeterancyMultipliers = shares,
            AvailableWithoutTransport = true,
            Transports = generation.AllTransports ? TransportsFor(unit, generation, transports) : [],
        };
    }

    /// <summary>The same numbers for every unit: the most at the lowest veterancy, and each level as a share of it.</summary>
    private static (int, IReadOnlyList<double>) SameNumbers(IReadOnlyList<int> counts)
    {
        int most = Math.Max(1, counts.DefaultIfEmpty(1).Max());
        return (most, [.. counts.Select(count => (double)count / most)]);
    }

    /// <summary>
    /// The units of a division that carry others (they have seats and tags they carry), in the order a unit's
    /// transports are listed: those that fly first, then the dearest first, then A to Z.
    /// </summary>
    /// <remarks>
    /// Sorted once for the whole division rather than once for each unit: the order does not depend on the unit
    /// carried, and the sort is stable, so picking each unit's transports from this list keeps the same order as
    /// sorting them on their own. With every unit of a side, sorting for each unit took seconds.
    /// </remarks>
    private static List<Unit> TransportsInOrder(List<Unit> taken) =>
        [.. taken.Where(transport => transport.CarriesTags is not null && transport.Seats is not null)
            .OrderBy(transport => transport.Flies ? 0 : 1)
            .ThenByDescending(transport => transport.Cost ?? double.NegativeInfinity)
            .ThenBy(transport => transport.Descriptor, NameComparer.Instance)];

    /// <summary>
    /// The transports a unit gets: every unit of the division that carries one of its tags and has the seats for it,
    /// in the order of <see cref="TransportsInOrder"/>. In a division of one side, only that side's transports; in one
    /// of both sides', any; and a unit of a nation in every division rides with either side.
    /// </summary>
    private static IReadOnlyList<string> TransportsFor(Unit unit, DivisionGeneration generation, List<Unit> transports)
    {
        HashSet<string> everywhere = [.. generation.EveryDivisionNations];
        // the units of nations in every division are of either side by design, as are all units of a both-sides division
        bool sameSideOnly = generation.Units != GeneratedUnits.Everything && !everywhere.Contains(unit.Nation);
        return [.. transports.Where(transport => CanCarry(transport, unit, sameSideOnly && !everywhere.Contains(transport.Nation)))
            .Select(transport => transport.Descriptor)];
    }

    /// <summary>
    /// Whether a transport can carry a unit (<see cref="Transports.CanCarry"/>), here without its deck-code number,
    /// which the export gives it, and across sides when the division takes both.
    /// </summary>
    private static bool CanCarry(Unit transport, Unit unit, bool sameSideOnly)
    {
        if (unit.SeatsTaken is not double seatsTaken) return false;
        if (transport.CarriesTags is not { } carries || transport.Seats is not double seats) return false;
        bool sharesATag = carries.Any(tag => unit.Tags.Contains(tag, StringComparer.Ordinal));
        return sharesATag && seatsTaken <= seats && (!sameSideOnly || transport.Side == unit.Side);
    }

    // ---- numbers as the game's divisions give them ----

    /// <summary>
    /// A unit's numbers as the game's divisions give them: the most generous rule any of them has for it; for a unit
    /// none has, the most generous of the rules of the most similar units (<see cref="SimilarUnitsRule"/>); else the
    /// fallback numbers.
    /// </summary>
    private (int, IReadOnlyList<double>) GameNumbers(Unit unit, DivisionGeneration generation)
    {
        Dictionary<string, DivisionUnit> rules = GameRules(generation);
        DivisionUnit rule = rules.TryGetValue(unit.Descriptor, out DivisionUnit? own) && GivesUnits(own) ? own : SimilarUnitsRule(unit, rules, generation);
        return (rule.UnitsPerCard ?? Fallback.UnitsPerCard!.Value, rule.VeterancyMultipliers);
    }

    /// <summary>Each unit's most generous rule in the game's divisions, leaving out the divisions and rules the generation ignores.</summary>
    private Dictionary<string, DivisionUnit> GameRules(DivisionGeneration generation)
    {
        string key = string.Join("|", generation.IgnoredDivisions) + "|" + generation.IgnoredRulesFrom;
        if (gameRules.TryGetValue(key, out Dictionary<string, DivisionUnit>? known)) return known;
        var best = new Dictionary<string, DivisionUnit>(StringComparer.Ordinal);
        foreach (Division division in game.Where(division => !IsIgnored(division, generation)))
        {
            foreach (DivisionUnit rule in division.Units)
            {
                if (rule.UnitsPerCard is null || IsIgnoredRule(rule, generation)) continue;
                if (!best.TryGetValue(rule.Unit, out DivisionUnit? before) || IsMoreGenerous(rule, before)) best[rule.Unit] = rule;
            }
        }
        gameRules[key] = best;
        return best;
    }

    /// <summary>Whether a game division is left out of what the numbers are learnt from: its rule's name holds a part the generation ignores.</summary>
    private static bool IsIgnored(Division division, DivisionGeneration generation) =>
        generation.IgnoredDivisions.Any(part => division.RuleName.Contains(part, StringComparison.Ordinal));

    /// <summary>Whether a rule gives so many cards or units that the generation ignores it.</summary>
    private static bool IsIgnoredRule(DivisionUnit rule, DivisionGeneration generation) =>
        generation.IgnoredRulesFrom is int from && (rule.Cards >= from || (rule.UnitsPerCard ?? 0) >= from || PeakCount(rule) >= from);

    /// <summary>Whether a rule gives any unit at all: some cards, and some units at some veterancy.</summary>
    private static bool GivesUnits(DivisionUnit rule) => rule.Cards > 0 && CountsByVeterancy(rule).Any(count => count > 0);

    /// <summary>
    /// Whether one rule for a unit is more generous than another: the most units on a card at any veterancy, then more
    /// cards, then more units per card. On a tie the rule found first stays.
    /// </summary>
    private static bool IsMoreGenerous(DivisionUnit rule, DivisionUnit than)
    {
        if (PeakCount(rule) != PeakCount(than)) return PeakCount(rule) > PeakCount(than);
        if (rule.Cards != than.Cards) return rule.Cards > than.Cards;
        return (rule.UnitsPerCard ?? 0) > (than.UnitsPerCard ?? 0);
    }

    /// <summary>The most units a card of a rule holds, at any veterancy.</summary>
    private static int PeakCount(DivisionUnit rule) => CountsByVeterancy(rule).DefaultIfEmpty(0).Max();

    /// <summary>
    /// The units a card of a rule holds at each veterancy, as the game works them out: units per card times the level's
    /// share, rounded. The shares are read as the game stores them (0.7 is a little under 0.7), so they are rounded to
    /// four places first, as the game's own sums come out.
    /// </summary>
    private static int[] CountsByVeterancy(DivisionUnit rule)
    {
        int perCard = rule.UnitsPerCard ?? 0;
        return [.. Enumerable.Range(0, VeterancyLevels).Select(level =>
        {
            double share = level < rule.VeterancyMultipliers.Count ? rule.VeterancyMultipliers[level] : 0;
            return share <= 0 ? 0 : (int)Math.Floor(Math.Round(perCard * share, 4, MidpointRounding.AwayFromZero) + 0.5);
        })];
    }

    // ---- a unit no division has: like the units most similar to it ----

    /// <summary>
    /// The numbers for a unit no game division has: among the units the game's divisions do have, the closest to it
    /// in cost (up to five) of the most similar kind there is, and the most generous of their rules, its veterancy
    /// levels made to fall from the lowest to the highest. Similarity, from the closest: the same category, the same
    /// type, both units or both buildings, and both command units or neither.
    /// </summary>
    private DivisionUnit SimilarUnitsRule(Unit unit, Dictionary<string, DivisionUnit> rules, DivisionGeneration generation)
    {
        string key = unit.Descriptor + "|" + string.Join("|", generation.IgnoredDivisions) + "|" + generation.IgnoredRulesFrom;
        if (similarRules.TryGetValue(key, out DivisionUnit? known)) return known;
        List<Unit> closest = ClosestSimilarUnits(unit, rules);
        DivisionUnit made = closest.Count == 0 ? Fallback : RepresentativeRule(closest.Select(similar => rules[similar.Descriptor]));
        similarRules[key] = made;
        return made;
    }

    /// <summary>The units closest to one, of the most similar kind any unit with a game rule is (see <see cref="SimilarUnitsRule"/>).</summary>
    private List<Unit> ClosestSimilarUnits(Unit unit, Dictionary<string, DivisionUnit> rules)
    {
        List<Unit> withRules = UnitsWithRules(rules);
        int leastUnlike = int.MaxValue;
        foreach (Unit other in withRules)
            if (other.Descriptor != unit.Descriptor) leastUnlike = Math.Min(leastUnlike, Unlikeness(other, unit));
        if (leastUnlike == int.MaxValue) return [];
        return [.. withRules
            .Where(other => other.Descriptor != unit.Descriptor && Unlikeness(other, unit) == leastUnlike)
            .OrderBy(other => Math.Abs((other.Cost ?? 0) - (unit.Cost ?? 0)))
            .ThenBy(other => other.Descriptor, NameComparer.Instance)
            .Take(SimilarUnitsCompared)];
    }

    /// <summary>
    /// The units a unit no division has can be likened to: those a deck can hold whose game rule gives units, in the
    /// order of the units to choose from. They are the same for every such unit, so they are found once rather than
    /// once for each of them (hundreds, each looking through every unit).
    /// </summary>
    private List<Unit> UnitsWithRules(Dictionary<string, DivisionUnit> rules)
    {
        if (unitsWithRules.TryGetValue(rules, out List<Unit>? known)) return known;
        List<Unit> found = [.. units.Where(other => IsDeckable(other) && rules.TryGetValue(other.Descriptor, out DivisionUnit? rule) && GivesUnits(rule))];
        unitsWithRules[rules] = found;
        return found;
    }

    /// <summary>How unlike another unit is: 0, the same in every way below, to 4, of another category.</summary>
    private static int Unlikeness(Unit other, Unit unit)
    {
        if (other.Factory != unit.Factory) return 4;
        if (TypeOf(other) != TypeOf(unit)) return 3;
        if (IsBuilding(other) != IsBuilding(unit)) return 2;
        return other.IsCommandUnit == unit.IsCommandUnit ? 0 : 1;
    }

    /// <summary>A unit's type, for likeness: what the game counts it as, else its role, else its category.</summary>
    private static string TypeOf(Unit unit) =>
        unit.StrategicType is not ("" or "NotCounted") ? unit.StrategicType : unit.Role != "" ? unit.Role : unit.Factory;

    /// <summary>Whether a unit is a building (BuildingDescriptors.ndf), such as a FOB.</summary>
    private static bool IsBuilding(Unit unit) => unit.File == "BuildingDescriptors.ndf";

    /// <summary>
    /// The rule of a unit like several others: the most generous of theirs (the most units at the lowest veterancy,
    /// then the next level, and so on; then the most cards), its levels made to fall from the lowest to the highest,
    /// and given as units per card and each level's share of it.
    /// </summary>
    private static DivisionUnit RepresentativeRule(IEnumerable<DivisionUnit> similar)
    {
        DivisionUnit best = similar.Aggregate((most, rule) => IsMoreGenerousByLevel(rule, most) ? rule : most);
        int[] counts = Falling(CountsByVeterancy(best));
        if (!counts.Any(count => count > 0)) return Fallback;
        int perCard = Math.Max(1, counts.Max());
        return new DivisionUnit { Unit = "", Cards = best.Cards, UnitsPerCard = perCard, VeterancyMultipliers = [.. counts.Select(count => count <= 0 ? 0 : (double)count / perCard)] };
    }

    /// <summary>Whether one rule gives more units than another, from the lowest veterancy up; then more cards; then by unit name.</summary>
    private static bool IsMoreGenerousByLevel(DivisionUnit rule, DivisionUnit than)
    {
        int[] counts = CountsByVeterancy(rule), thanCounts = CountsByVeterancy(than);
        for (int level = 0; level < VeterancyLevels; level++)
            if (counts[level] != thanCounts[level]) return counts[level] > thanCounts[level];
        if (rule.Cards != than.Cards) return rule.Cards > than.Cards;
        return NameComparer.Instance.Compare(rule.Unit, than.Unit) < 0;
    }

    /// <summary>Counts by veterancy made never to rise: a level with more units than the one below it is cut to that one's.</summary>
    private static int[] Falling(int[] counts)
    {
        int[] falling = [.. counts];
        int last = 0;
        for (int level = 0; level < falling.Length; level++)
        {
            if (falling[level] <= 0) continue;
            if (last > 0 && falling[level] > last) falling[level] = last;
            last = falling[level];
        }
        return falling;
    }

    /// <summary>Descriptors in the order a person reads them, the same on every computer.</summary>
    private sealed class NameComparer : IComparer<string>
    {
        public static readonly NameComparer Instance = new();

        public int Compare(string? left, string? right) => NameOrder.Compare(left, right, CompareOptions.None);
    }
}
