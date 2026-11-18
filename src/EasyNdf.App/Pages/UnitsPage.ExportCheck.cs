using EasyNdf.App.Mods;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The check Run export makes of the divisions and units of the mods that are on before writing them: what the game may
/// refuse, or players may not expect, said before the export rather than found in the game.
/// </summary>
public partial class UnitsPage
{
    /// <summary>What may not work in the game in what one mod changes, one line each.</summary>
    /// <param name="Mod">The mod's name.</param>
    /// <param name="Warnings">Its warnings.</param>
    public sealed record ModWarnings(string Mod, List<string> Warnings);

    /// <summary>
    /// What may not work in the game in the divisions and units the mods that are on change or add, by mod in the Mods
    /// tab's order; only mods with warnings. The divisions are checked as all those mods leave them, one after the other,
    /// as an export writes them, and a division's warnings go to the last mod that changes it. None when nothing is
    /// wrong, or when the Armory has not read the game's divisions (then nothing can be checked).
    /// </summary>
    public List<ModWarnings> DivisionWarnings()
    {
        if (GameDivisions.Count == 0 || loaded is null) return [];
        IReadOnlyList<Mod> mods = ModStack.RunningFor(Game);
        IReadOnlyList<Division> divisions = DivisionsBeforeArmoryChanges;
        foreach (Mod mod in mods) divisions = DivisionChanges.Apply(divisions, mod, Rosters);
        var lastToChange = new Dictionary<string, Mod>(StringComparer.Ordinal);
        foreach (Mod mod in mods)
            foreach (ModDivision entry in mod.Divisions.Where(entry => !entry.IsRemoved)) lastToChange[entry.Descriptor] = mod;
        var found = new List<ModWarnings>();
        foreach (Mod mod in mods)
        {
            var warnings = new List<string>();
            WarnAboutArmor(mod, warnings);
            foreach (Division division in divisions.Where(division => ReferenceEquals(lastToChange.GetValueOrDefault(division.Descriptor), mod)))
            {
                if (division.EmblemTexture == "" && division.EmblemFile is null)
                    warnings.Add(Texts.Get("Divisions.Check.NoEmblem", ("division", division.Name)));
                WarnAboutDlc(mod, division, warnings);
                WarnAboutCommandUnits(division, warnings);
                WarnAboutTexts(mod, division, warnings);
                WarnAboutCards(division, warnings);
                WarnAboutBattlegroup(division, warnings);
                bool generated = DivisionChanges.EntryOf(mod, division.Descriptor) is { IsGenerated: true };
                foreach (DivisionUnit rule in UnitsSetByHand(mod, division)) WarnAboutTransports(division, rule, warnings, checkAccepted: !generated);
            }
            if (warnings.Count > 0) found.Add(new ModWarnings(mod.Name, warnings));
        }
        return found;
    }

    /// <summary>
    /// A unit the mod adds or changes with an armour level its armour family does not have: GenerateMod stops on it
    /// ("Index must be between 1 and 4 for this family").
    /// </summary>
    private void WarnAboutArmor(Mod mod, List<string> warnings)
    {
        foreach (ModUnit added in mod.Units)
            if (GameUnits.FirstOrDefault(unit => unit.Descriptor == added.MadeFrom) is Unit madeFrom)
                WarnAboutArmor(added, madeFrom, added.Name, warnings);
        foreach (UnitEdit edit in mod.UnitEdits)
            if (GameUnits.FirstOrDefault(unit => unit.Descriptor == edit.Descriptor) is Unit gameUnit)
                WarnAboutArmor(edit, gameUnit, edit.Name ?? gameUnit.Name, warnings);
    }

    /// <summary>The armour levels a unit is given that its armour families do not have, each as a warning.</summary>
    private void WarnAboutArmor(UnitValues values, Unit unit, string unitName, List<string> warnings)
    {
        foreach ((string side, int level, string family) in values.ArmorOutOfRange(unit, gameUnits?.Catalog.ArmorLevels))
        {
            (int lowest, int highest) = ArmorLevels.RangeOf(gameUnits?.Catalog.ArmorLevels, family)!.Value;
            warnings.Add(Texts.Get("Units.Check.Armor", ("unit", unitName), ("side", SideName(side)), ("level", level),
                ("family", UnitNames.ArmorFamily(family)), ("lowest", lowest), ("highest", highest)));
        }
    }

    /// <summary>A side's name, as the Unit pane's armour table names it.</summary>
    private static string SideName(string side) => side switch
    {
        "Front" => Texts.Get("Units.Pane.Front"),
        "Sides" => Texts.Get("Units.Pane.Sides"),
        "Rear" => Texts.Get("Units.Pane.Rear"),
        _ => Texts.Get("Units.Pane.Top"),
    };

    /// <summary>
    /// The division's units set by hand: all of them, but in a generated division only those the user changed. A
    /// generated division's own transports follow the rules it was made by (nations in every division and "Everything"
    /// divisions take any side's transports, as a large mod's generator does), which the game accepts though they fall
    /// outside the rules a transport chosen by hand is checked by; a unit the user changed keeps them (a change of its
    /// cards keeps its transports), so there only whether it can be taken at all is checked.
    /// </summary>
    private static IEnumerable<DivisionUnit> UnitsSetByHand(Mod mod, Division division)
    {
        if (DivisionChanges.EntryOf(mod, division.Descriptor) is not { IsGenerated: true } entry) return division.Units;
        HashSet<string> changed = [.. entry.Added.Select(rule => rule.Unit)];
        return division.Units.Where(rule => changed.Contains(rule.Unit));
    }

    /// <summary>
    /// Units the mod put in a Steel Division 2 DLC division, from before the Armory refused them: the export leaves
    /// them out, as the game stops on a card a mod adds to a DLC division (SteelDivisionDivisions.IsInDlc).
    /// </summary>
    private void WarnAboutDlc(Mod mod, Division division, List<string> warnings)
    {
        if (!SteelDivisionDivisions.IsInDlc(division.Descriptor) || DivisionChanges.EntryOf(mod, division.Descriptor) is not { IsNew: false } entry) return;
        foreach (DivisionUnit rule in entry.Added)
            warnings.Add(Texts.Get("Divisions.Check.DlcUnitLeftOut", ("division", division.Name),
                ("unit", unitsByDescriptor.GetValueOrDefault(rule.Unit)?.Name ?? rule.Unit)));
    }

    /// <summary>A division with no command unit: the game does not accept a deck without one.</summary>
    private void WarnAboutCommandUnits(Division division, List<string> warnings)
    {
        bool hasOne = division.Units.Any(rule => unitsByDescriptor.TryGetValue(rule.Unit, out Unit? unit) && unit.IsCommandUnit);
        if (!hasOne) warnings.Add(Texts.Get("Divisions.Check.NoCommandUnit", ("division", division.Name)));
    }

    /// <summary>
    /// An empty summary or history the mod gave the division: a new division's, or a game division's the mod emptied. A
    /// game division's own texts are the game's to give (the app may not have found them in its dictionaries).
    /// </summary>
    private void WarnAboutTexts(Mod mod, Division division, List<string> warnings)
    {
        if (DivisionChanges.EntryOf(mod, division.Descriptor) is not ModDivision entry) return;
        if (IsEmpty(entry.IsNew ? division.Summary : entry.Summary))
            warnings.Add(Texts.Get("Divisions.Check.NoSummary", ("division", division.Name)));
        // a generated division's history is left empty unless the user writes one (a large mod's give none): not a slip
        // a game whose divisions have no history (Steel Division 2) is not asked for one
        if (!entry.IsGenerated && DivisionChangeRules.Has(Game, DivisionDetail.History) && IsEmpty(entry.IsNew ? division.History : entry.History))
            warnings.Add(Texts.Get("Divisions.Check.NoHistory", ("division", division.Name)));
    }

    /// <summary>Whether a text is there but empty; null is no text set by the mod.</summary>
    private static bool IsEmpty(string? text) => text is not null && text.Trim() == "";

    /// <summary>
    /// What is wrong with the division's Official Battlegroup by its rules, the mod's changes to either included:
    /// GenerateMod stops on a battlegroup whose card the division does not allow.
    /// </summary>
    private void WarnAboutBattlegroup(Division division, List<string> warnings)
    {
        if (division.Battlegroup is not Battlegroup battlegroup) return;
        List<BattlegroupProblem> problems = BattlegroupCheck.Problems(battlegroup.Cards, division, unit => unitsByDescriptor.GetValueOrDefault(unit)?.Factory);
        // cards of units the source does not have are said once for the battlegroup, as its view says them
        List<string> unknown = [.. problems.Where(problem => problem.Kind == BattlegroupProblemKind.UnitNotInDivision && !unitsByDescriptor.ContainsKey(problem.Subject))
            .Select(problem => problem.Subject).Distinct()];
        if (unknown.Count > 1)
            warnings.Add(Texts.Get("Divisions.Check.Battlegroup", ("division", division.Name), ("problem", Divisions.BattlegroupView.UnknownUnitsText(unknown, unitsByDescriptor))));
        foreach (BattlegroupProblem problem in problems.Where(problem => unknown.Count <= 1 || !unknown.Contains(problem.Subject) || problem.Kind != BattlegroupProblemKind.UnitNotInDivision))
            warnings.Add(Texts.Get("Divisions.Check.Battlegroup", ("division", division.Name), ("problem", Divisions.BattlegroupView.ProblemText(problem, unitsByDescriptor))));
    }

    /// <summary>A type with units in the division but no card to take them with.</summary>
    private void WarnAboutCards(Division division, List<string> warnings)
    {
        IEnumerable<string> types = division.Units
            .Select(rule => unitsByDescriptor.GetValueOrDefault(rule.Unit)?.Factory)
            .OfType<string>()
            .Distinct();
        foreach (string type in types)
            if (!division.Costs.TryGetValue(type, out IReadOnlyList<int>? costs) || costs.Count == 0)
                warnings.Add(Texts.Get("Divisions.Check.NoCards", ("division", division.Name), ("type", UnitNames.Factory(type))));
    }

    /// <summary>
    /// A unit that can ride in a transport but has none and cannot be taken without one, so it cannot be taken at all;
    /// and a transport the game does not accept for the unit.
    /// </summary>
    /// <param name="checkAccepted">Whether its transports are checked against those the game accepts: not in a generated division.</param>
    private void WarnAboutTransports(Division division, DivisionUnit rule, List<string> warnings, bool checkAccepted)
    {
        if (!unitsByDescriptor.TryGetValue(rule.Unit, out Unit? unit) || unit.SeatsTaken is null) return;
        if (rule.Transports.Count == 0 && !rule.AvailableWithoutTransport)
            warnings.Add(Texts.Get("Divisions.Check.NoWayToTake", ("division", division.Name), ("unit", unit.Name)));
        if (!checkAccepted) return;
        HashSet<string> accepted = [.. TransportsFor(unit).Select(transport => transport.Descriptor)];
        foreach (string transport in rule.Transports.Where(transport => !accepted.Contains(transport)))
            warnings.Add(Texts.Get("Divisions.Check.TransportNotAccepted", ("division", division.Name), ("unit", unit.Name),
                ("transport", unitsByDescriptor.GetValueOrDefault(transport)?.Name ?? transport)));
    }
}
