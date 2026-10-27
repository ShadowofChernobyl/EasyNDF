using EasyNdf.App.Controls;
using EasyNdf.App.Divisions;
using EasyNdf.App.Text;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Pages;

/// <summary>
/// Copying how one unit is had in a division onto others: its cards, units per card and veterancy, or its transports.
/// What is copied is kept in the app while it runs, not on the system clipboard: it only makes sense here.
/// </summary>
public partial class UnitsPage
{
    /// <summary>The cards, units per card and veterancy copied from a unit; null before any are.</summary>
    private DivisionUnit? copiedCards;

    /// <summary>The transports copied from a unit, and whether it could be taken without one; null before any are.</summary>
    private DivisionUnit? copiedTransports;

    /// <summary>
    /// The menu's copy and paste choices for units of the division shown: copy from the unit right-clicked, paste onto
    /// it or onto every unit selected. None outside a division, or for a unit it does not have.
    /// </summary>
    /// <param name="clicked">The unit right-clicked.</param>
    /// <param name="units">The units a paste goes onto: the one right-clicked, or those selected.</param>
    private MenuChoice?[] DivisionClipboardChoices(Unit clicked, IReadOnlyList<Unit> units)
    {
        if (chosenDivision is not Division division || DivisionMarks.UnitIn(division, clicked.Descriptor) is not DivisionUnit rule) return [];
        List<Unit> inDivision = [.. units.Where(InChosenDivision)];
        bool anyRides = inDivision.Any(unit => unit.SeatsTaken is not null);
        return
        [
            new MenuChoice(Texts.Get("Divisions.Menu.CopyCards"), () => copiedCards = rule),
            new MenuChoice(Texts.Get("Divisions.Menu.PasteCards"), () => PasteCards(division, inDivision), IsEnabled: copiedCards is not null),
            clicked.SeatsTaken is null ? null : new MenuChoice(Texts.Get("Divisions.Menu.CopyTransports"), () => copiedTransports = rule),
            anyRides ? new MenuChoice(Texts.Get("Divisions.Menu.PasteTransports"), () => PasteTransports(division, inDivision), IsEnabled: copiedTransports is not null) : null,
        ];
    }

    /// <summary>The cards, units per card and veterancy copied, given to units of the division, as one change.</summary>
    private void PasteCards(Division division, IReadOnlyList<Unit> units)
    {
        if (copiedCards is not DivisionUnit copied) return;
        List<DivisionUnit> changed = [.. RulesOf(division, units).Select(rule => rule with
        {
            Cards = copied.Cards,
            UnitsPerCard = copied.UnitsPerCard,
            UnitsPerCardByPhase = copied.UnitsPerCardByPhase,
            VeterancyMultipliers = copied.VeterancyMultipliers,
        })];
        ChangeRules(division, changed);
    }

    /// <summary>
    /// The transports copied, given to units of the division that can ride in one, as one change. A unit gets only the
    /// transports the game accepts for it, so a paste never makes one the game would refuse.
    /// </summary>
    private void PasteTransports(Division division, IReadOnlyList<Unit> units)
    {
        if (copiedTransports is not DivisionUnit copied) return;
        var changed = new List<DivisionUnit>();
        foreach (Unit unit in units.Where(unit => unit.SeatsTaken is not null))
        {
            if (DivisionMarks.UnitIn(division, unit.Descriptor) is not DivisionUnit rule) continue;
            HashSet<string> accepted = [.. TransportsFor(unit).Select(transport => transport.Descriptor)];
            List<string> transports = [.. copied.Transports.Where(accepted.Contains)];
            changed.Add(rule with { Transports = transports, AvailableWithoutTransport = copied.AvailableWithoutTransport || transports.Count == 0 });
        }
        ChangeRules(division, changed);
    }

    /// <summary>The division's lines for some units, those it has.</summary>
    private static IEnumerable<DivisionUnit> RulesOf(Division division, IReadOnlyList<Unit> units) =>
        units.Select(unit => DivisionMarks.UnitIn(division, unit.Descriptor)).OfType<DivisionUnit>();
}
