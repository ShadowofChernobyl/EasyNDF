using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.Data.Divisions;

/// <summary>Which pick of its role a generated card is.</summary>
public enum BattlegroupPick
{
    /// <summary>The role's strongest unit, whatever it costs.</summary>
    Best,

    /// <summary>The role's unit that gives the most for its cost.</summary>
    GoodValue,

    /// <summary>A cheap unit of the role that still does the job (at least half as strong as the best).</summary>
    Cheap,
}

/// <summary>A card the battlegroup generator chose, and why.</summary>
/// <param name="Card">The card.</param>
/// <param name="Role">The unit's role (the game's UnitRole, such as "tank_A"; its category when it has none).</param>
/// <param name="Pick">Which pick of its role it is.</param>
/// <param name="Score">Its score, 0 to 1,000: its marks weighed by the settings.</param>
/// <param name="NextUnit">The unit of its role that came next for the same pick; null when none did.</param>
/// <param name="NextScore">That unit's score.</param>
public sealed record GeneratedCard(BattlegroupCard Card, string Role, BattlegroupPick Pick, double Score, string? NextUnit, double NextScore);

/// <summary>
/// EasyNDF's battlegroup generator: chooses a generated division's Official Battlegroup from the division's own units,
/// by the settings of its Generation Settings window (<see cref="BattlegroupGeneration"/>). Within each category
/// (tanks, infantry…) it gives every unit a mark from 0 to 1,000 on each measure against the others of its category,
/// weighs the marks by the sliders into a score, and fills each role with its best, good-value and cheap units; then it
/// keeps the cards the settings rank highest, as many as each category may hold, within the points allowed.
/// </summary>
public static class BattlegroupGenerator
{
    /// <summary>The highest mark a unit can have on a measure: that of the best of its category.</summary>
    private const double TopMark = 1000;

    /// <summary>How strong a cheap pick must be, as a share of its role's best unit's score.</summary>
    private const double CheapAtLeast = 0.5;

    /// <summary>The measures a unit is marked on; value for points and variety are worked out from these.</summary>
    private static readonly BattlegroupMeasure[] MarkedMeasures =
    [
        BattlegroupMeasure.AgainstArmour, BattlegroupMeasure.AgainstInfantry, BattlegroupMeasure.AgainstAircraft,
        BattlegroupMeasure.Survivability, BattlegroupMeasure.Mobility, BattlegroupMeasure.ReconAndOptics,
    ];

    /// <summary>A unit of the division, with what the generator knows of it.</summary>
    private sealed record Candidate(Unit Unit, DivisionUnit Rule, string Role, double Score, double ValueMark);

    /// <summary>A card chosen, before the categories are trimmed: its candidate, pick, place in its role, and rank.</summary>
    private sealed record Choice(Candidate Candidate, BattlegroupPick Pick, int PlaceInRole, double Priority, Candidate? Next);

    /// <summary>
    /// The battlegroup the settings make from the division's units: its cards, category by category in the order of the
    /// division's card costs, the highest-ranked first.
    /// </summary>
    /// <param name="division">The division, with its units, card costs and activation points.</param>
    /// <param name="units">The units, by descriptor, with their weapons.</param>
    /// <param name="settings">How the battlegroup is made.</param>
    public static List<GeneratedCard> Cards(Division division, IReadOnlyDictionary<string, Unit> units, BattlegroupGeneration settings)
    {
        List<Choice> kept = [];
        foreach (IGrouping<string, Candidate> category in Candidates(division, units, settings).GroupBy(candidate => candidate.Unit.Factory))
            kept.AddRange(ChoicesFor([.. category], settings).Take(RoomFor(category.Key, division, settings)));
        TrimToPoints(kept, division, settings);
        List<string> order = [.. division.Costs.Keys];
        return [.. kept
            .OrderBy(choice => order.IndexOf(choice.Candidate.Unit.Factory) is int at && at >= 0 ? at : order.Count)
            .ThenByDescending(choice => choice.Priority)
            .Select(CardOf)];
    }

    // ---- the candidates, marked and scored ----

    /// <summary>
    /// The division's units a card can hold (known, with cards and some units at some veterancy), each scored against
    /// the others of its category.
    /// </summary>
    private static List<Candidate> Candidates(Division division, IReadOnlyDictionary<string, Unit> units, BattlegroupGeneration settings)
    {
        var usable = division.Units
            .Where(rule => rule.Cards > 0 && LowestVeterancy(rule) is not null && units.ContainsKey(rule.Unit))
            .Select(rule => (Unit: units[rule.Unit], Rule: rule))
            .ToList();
        var candidates = new List<Candidate>();
        foreach (var category in usable.GroupBy(each => each.Unit.Factory))
        {
            Dictionary<BattlegroupMeasure, double> highest = MarkedMeasures.ToDictionary(measure => measure, measure => category.Max(each => Raw(each.Unit, measure)));
            List<(Unit Unit, DivisionUnit Rule, double Score)> scored = [.. category.Select(each => (each.Unit, each.Rule, Score(each.Unit, highest, settings)))];
            double bestValue = scored.Max(each => ValueOf(each.Score, each.Unit));
            foreach ((Unit unit, DivisionUnit rule, double score) in scored)
            {
                double valueMark = bestValue > 0 ? ValueOf(score, unit) / bestValue * TopMark : 0;
                candidates.Add(new Candidate(unit, rule, RoleOf(unit), score, valueMark));
            }
        }
        return candidates;
    }

    /// <summary>A unit's score: its marks on the measures, each weighed by its slider, as a mark from 0 to 1,000.</summary>
    private static double Score(Unit unit, Dictionary<BattlegroupMeasure, double> highest, BattlegroupGeneration settings)
    {
        double weights = MarkedMeasures.Sum(measure => settings.WeightOf(measure));
        if (weights == 0) return 0;
        return MarkedMeasures.Sum(measure => settings.WeightOf(measure) * Mark(Raw(unit, measure), highest[measure])) / weights;
    }

    /// <summary>A unit's mark on a measure: how close it comes to the best of its category, from 0 to 1,000.</summary>
    private static double Mark(double raw, double highest) => highest > 0 ? raw / highest * TopMark : 0;

    /// <summary>What a unit gives for its cost: its score per point (its score alone when it costs nothing).</summary>
    private static double ValueOf(double score, Unit unit) => unit.Cost is double cost && cost > 0 ? score / cost : score;

    /// <summary>A unit's role: the game's UnitRole ("tank_A"); its category when it has none.</summary>
    private static string RoleOf(Unit unit) => unit.Role != "" ? unit.Role : unit.Factory;

    /// <summary>
    /// A unit's raw number on a measure, from its weapons and stats; only how it compares with the others of its
    /// category counts (<see cref="Mark"/>).
    /// </summary>
    private static double Raw(Unit unit, BattlegroupMeasure measure) => measure switch
    {
        BattlegroupMeasure.AgainstArmour => unit.Weapons.Where(IsAntiArmour).Select(weapon => weapon.DamageLevel * Math.Sqrt(weapon.Range ?? 0)).DefaultIfEmpty(0).Max(),
        BattlegroupMeasure.AgainstInfantry => unit.Weapons.Where(weapon => !IsAntiAir(weapon)).Sum(InfantryFire),
        BattlegroupMeasure.AgainstAircraft => unit.Weapons.Where(IsAntiAir).Select(weapon => weapon.Count * (weapon.Damage ?? 1) * (weapon.Range ?? 0)).DefaultIfEmpty(0).Max(),
        BattlegroupMeasure.Survivability => (unit.Strength ?? 0) * (1 + (unit.Armor.Front ?? 0) / 10.0),
        BattlegroupMeasure.Mobility => unit.Speed ?? unit.RoadSpeed ?? 0,
        BattlegroupMeasure.ReconAndOptics => (unit.Optics ?? 0) * Math.Sqrt(unit.Vision ?? 0),
        _ => 0,
    };

    /// <summary>Whether a weapon pierces armour: its damage is armour-piercing ("ap", and AP missiles and rockets), and it is not anti-air.</summary>
    private static bool IsAntiArmour(UnitWeapon weapon) =>
        !IsAntiAir(weapon) && weapon.DamageFamily.Contains("ap", StringComparison.OrdinalIgnoreCase) && weapon.DamageLevel > 0;

    /// <summary>Whether a weapon is made against aircraft (by its ammunition's name, as the Weapons sub-tab sorts them).</summary>
    private static bool IsAntiAir(UnitWeapon weapon) => WeaponCategories.Of(weapon.Ammunition, WeaponKinds.Of(weapon)) == WeaponCategory.AntiAir;

    /// <summary>
    /// How much a weapon deals to infantry and light targets: its damage and a tenth of its suppression, times how
    /// many, per second between salvos, longer reach counting for a little more.
    /// </summary>
    private static double InfantryFire(UnitWeapon weapon)
    {
        double perSalvo = ((weapon.Damage ?? 0) + (weapon.Suppression ?? 0) / 10) * weapon.Count;
        return perSalvo / Math.Max(1, weapon.SalvoSeconds ?? 1) * Math.Sqrt(Math.Sqrt(weapon.Range ?? 0) + 1);
    }

    // ---- the picks of each role, ranked ----

    /// <summary>
    /// A category's picks, highest-ranked first: each role's best, good-value and cheap units (those the settings
    /// take), ranked by score and value for points; the more variety, the more each role's first pick goes before
    /// another role's second.
    /// </summary>
    private static List<Choice> ChoicesFor(List<Candidate> category, BattlegroupGeneration settings)
    {
        var choices = new List<Choice>();
        foreach (IGrouping<string, Candidate> role in category.GroupBy(candidate => candidate.Role))
        {
            List<Candidate> strongest = [.. role.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Unit.Descriptor, StringComparer.Ordinal)];
            var taken = new List<(Candidate Candidate, BattlegroupPick Pick, Candidate? Next)>();
            if (settings.TakesBest) Take(taken, strongest, BattlegroupPick.Best);
            if (settings.TakesGoodValue)
                Take(taken, [.. role.OrderByDescending(candidate => candidate.ValueMark).ThenBy(candidate => candidate.Unit.Descriptor, StringComparer.Ordinal)], BattlegroupPick.GoodValue);
            if (settings.TakesCheap)
            {
                double strong = strongest[0].Score * CheapAtLeast;
                Take(taken, [.. role.Where(candidate => candidate.Score >= strong).OrderBy(candidate => candidate.Unit.Cost ?? 0).ThenByDescending(candidate => candidate.Score)], BattlegroupPick.Cheap);
            }
            for (int place = 0; place < taken.Count; place++)
                choices.Add(new Choice(taken[place].Candidate, taken[place].Pick, place, Priority(taken[place].Candidate, place, settings), taken[place].Next));
        }
        return [.. choices.OrderByDescending(choice => choice.Priority).ThenBy(choice => choice.Candidate.Unit.Descriptor, StringComparer.Ordinal)];
    }

    /// <summary>Takes the first of the ranked units not taken yet for a pick, remembering the one that came next.</summary>
    private static void Take(List<(Candidate Candidate, BattlegroupPick Pick, Candidate? Next)> taken, List<Candidate> ranked, BattlegroupPick pick)
    {
        List<Candidate> left = [.. ranked.Where(candidate => !taken.Any(each => each.Candidate == candidate))];
        if (left.Count > 0) taken.Add((left[0], pick, left.Count > 1 ? left[1] : null));
    }

    /// <summary>
    /// How highly a pick ranks: its score and its value for points, mixed by the value slider; less for each pick of
    /// its role before it, by the variety slider.
    /// </summary>
    private static double Priority(Candidate candidate, int placeInRole, BattlegroupGeneration settings)
    {
        double value = settings.WeightOf(BattlegroupMeasure.ValueForPoints) / 100.0;
        double variety = settings.WeightOf(BattlegroupMeasure.Variety) / 100.0;
        return candidate.Score * (1 - value) + candidate.ValueMark * value - variety * TopMark * placeInRole;
    }

    // ---- size ----

    /// <summary>The cards a category may hold: the settings' cards per category, no more than the division has card slots for.</summary>
    private static int RoomFor(string category, Division division, BattlegroupGeneration settings) =>
        division.Costs.TryGetValue(category, out IReadOnlyList<int>? costs) ? Math.Min(settings.CardsPerCategory, costs.Count) : 0;

    /// <summary>Drops the lowest-ranked cards until the battlegroup costs no more than the points allowed.</summary>
    private static void TrimToPoints(List<Choice> kept, Division division, BattlegroupGeneration settings)
    {
        int? allowed = settings.PointsAtMost ?? division.ActivationPoints;
        if (allowed is not int most) return;
        while (kept.Count > 0 && BattlegroupCheck.Points([.. kept.Select(CardOf).Select(card => card.Card)], division, unit => FactoryOf(kept, unit)) > most)
            kept.Remove(kept.MinBy(choice => choice.Priority)!);
    }

    /// <summary>The category of a kept card's unit.</summary>
    private static string? FactoryOf(List<Choice> kept, string unit) => kept.FirstOrDefault(choice => choice.Candidate.Unit.Descriptor == unit)?.Candidate.Unit.Factory;

    // ---- the card ----

    /// <summary>
    /// A pick's card: at the lowest veterancy the division offers its unit; in the first transport the division gives it
    /// when it cannot go without one, else on its own.
    /// </summary>
    private static GeneratedCard CardOf(Choice choice)
    {
        DivisionUnit rule = choice.Candidate.Rule;
        string? transport = !rule.AvailableWithoutTransport && rule.Transports.Count > 0 ? rule.Transports[0] : null;
        var card = new BattlegroupCard(rule.Unit, LowestVeterancy(rule) ?? 0, transport);
        return new GeneratedCard(card, choice.Candidate.Role, choice.Pick, choice.Candidate.Score, choice.Next?.Unit.Descriptor, choice.Next?.Score ?? 0);
    }

    /// <summary>The lowest veterancy level at which a card of the unit holds any unit; null when none does.</summary>
    private static int? LowestVeterancy(DivisionUnit rule)
    {
        for (int level = 0; level < rule.VeterancyMultipliers.Count; level++)
            if (rule.VeterancyMultipliers[level] > 0 && (rule.UnitsPerCard ?? 0) * rule.VeterancyMultipliers[level] >= 0.5) return level;
        return null;
    }
}
