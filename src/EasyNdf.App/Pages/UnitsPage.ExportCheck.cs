using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// The check Run export makes of the open mod's divisions and units before writing them: what the game may refuse, or players
/// may not expect, said before the export rather than found in the game.
/// </summary>
public partial class UnitsPage
{
    /// <summary>
    /// What may not work in the game in the divisions the open mod changes or adds, one line each; none when nothing is
    /// wrong, or when the Armory has not read the game's divisions (then nothing can be checked).
    /// </summary>
    public List<string> DivisionWarnings()
    {
        if (GameDivisions.Count == 0 || loaded is null) return [];
        var warnings = new List<string>();
        WarnAboutArmor(warnings);
        HashSet<string> changed = [.. OpenMod.Divisions.Where(entry => !entry.IsRemoved).Select(entry => entry.Descriptor)];
        if (changed.Count == 0) return warnings;
        foreach (Division division in DivisionChanges.Apply(DivisionsBeforeArmoryChanges, OpenMod, Rosters).Where(division => changed.Contains(division.Descriptor)))
        {
            if (division.EmblemTexture == "" && division.EmblemFile is null)
                warnings.Add(Texts.Get("Divisions.Check.NoEmblem", ("division", division.Name)));
            WarnAboutCommandUnits(division, warnings);
            WarnAboutTexts(division, warnings);
            WarnAboutCards(division, warnings);
            WarnAboutBattlegroup(division, warnings);
            foreach (DivisionUnit rule in division.Units) WarnAboutTransports(division, rule, warnings);
        }
        return warnings;
    }

    /// <summary>
    /// A unit the mod adds or changes with an armour level its armour family does not have: GenerateMod stops on it
    /// ("Index must be between 1 and 4 for this family").
    /// </summary>
    private void WarnAboutArmor(List<string> warnings)
    {
        foreach (ModUnit added in OpenMod.Units)
            if (GameUnits.FirstOrDefault(unit => unit.Descriptor == added.MadeFrom) is Unit madeFrom)
                WarnAboutArmor(added, madeFrom, added.Name, warnings);
        foreach (UnitEdit edit in OpenMod.UnitEdits)
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
    private void WarnAboutTexts(Division division, List<string> warnings)
    {
        if (DivisionChanges.EntryOf(OpenMod, division.Descriptor) is not ModDivision entry) return;
        if (IsEmpty(entry.IsNew ? division.Summary : entry.Summary))
            warnings.Add(Texts.Get("Divisions.Check.NoSummary", ("division", division.Name)));
        if (IsEmpty(entry.IsNew ? division.History : entry.History))
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
        foreach (BattlegroupProblem problem in BattlegroupCheck.Problems(battlegroup.Cards, division, unit => unitsByDescriptor.GetValueOrDefault(unit)?.Factory))
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
    private void WarnAboutTransports(Division division, DivisionUnit rule, List<string> warnings)
    {
        if (!unitsByDescriptor.TryGetValue(rule.Unit, out Unit? unit) || unit.SeatsTaken is null) return;
        if (rule.Transports.Count == 0 && !rule.AvailableWithoutTransport)
            warnings.Add(Texts.Get("Divisions.Check.NoWayToTake", ("division", division.Name), ("unit", unit.Name)));
        HashSet<string> accepted = [.. TransportsFor(unit).Select(transport => transport.Descriptor)];
        foreach (string transport in rule.Transports.Where(transport => !accepted.Contains(transport)))
            warnings.Add(Texts.Get("Divisions.Check.TransportNotAccepted", ("division", division.Name), ("unit", unit.Name),
                ("transport", unitsByDescriptor.GetValueOrDefault(transport)?.Name ?? transport)));
    }
}
