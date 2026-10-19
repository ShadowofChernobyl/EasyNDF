using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Divisions tab's table columns: the Units table's own, with three of the division's after the unit's role, each
/// changed in the table: how many cards the division has of each unit, how many units come in each card, and the
/// share of those units at each veterancy level.
/// </summary>
public static class DivisionTableColumns
{
    /// <summary>The widths of the boxes: a whole number, and a share such as 0.68.</summary>
    private const double WholeNumberWidth = 40, ShareWidth = 38;

    /// <summary>The columns for a division's units.</summary>
    /// <param name="division">The division shown.</param>
    /// <param name="keep">Keeps a unit's rule as changed in the table, in the open mod.</param>
    public static IReadOnlyList<UnitTableColumn> For(Division division, Action<DivisionUnit> keep)
    {
        // a unit the division lists twice is shown as it is the first time
        Dictionary<string, DivisionUnit> rules = division.Units.GroupBy(rule => rule.Unit).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        DivisionUnit? RuleOf(Unit unit) => rules.GetValueOrDefault(unit.Descriptor);
        List<UnitTableColumn> columns = [.. UnitTableColumn.All];
        columns.InsertRange(3,
        [
            new UnitTableColumn("Cards", 64, unit => RuleOf(unit)?.Cards, Smallest: 56,
                Editor: unit => RuleOf(unit) is DivisionUnit rule ? CardsBox(rule, keep) : null),
            new UnitTableColumn("PerCard", 100, unit => RuleOf(unit) is DivisionUnit rule ? UnitsPerCard(rule) : null,
                Editor: unit => RuleOf(unit) is { UnitsPerCard: not null } rule ? UnitsPerCardBox(rule, keep) : null),
            new UnitTableColumn("Veterancy", 190, null, Smallest: 120, Largest: 260,
                Editor: unit => RuleOf(unit) is DivisionUnit rule ? VeterancyBoxes(rule, keep) : null),
        ]);
        return columns;
    }

    /// <summary>The cards of the unit the division has (MaxPackNumber): a whole number, 0 or more.</summary>
    private static EditBox CardsBox(DivisionUnit rule, Action<DivisionUnit> keep) =>
        new(Texts.Number(rule.Cards), WholeNumberWidth, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int cards) || cards < 0) return false;
            keep(rule with { Cards = cards });
            return true;
        });

    /// <summary>The units in each card (NumberOfUnitInPack): a whole number, 1 or more.</summary>
    private static EditBox UnitsPerCardBox(DivisionUnit rule, Action<DivisionUnit> keep) =>
        new(Texts.Number(rule.UnitsPerCard ?? 0), WholeNumberWidth, typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, Texts.Catalog.Culture, out int perCard) || perCard < 1) return false;
            keep(rule with { UnitsPerCard = perCard });
            return true;
        });

    /// <summary>
    /// The share of the units per card at each veterancy level, from the lowest (NumberOfUnitInPackXPMultiplier): a box
    /// for each, 0 where the level is not offered; the tooltip gives the units that makes.
    /// </summary>
    private static StackPanel VeterancyBoxes(DivisionUnit rule, Action<DivisionUnit> keep)
    {
        var boxes = new StackPanel { Orientation = Orientation.Horizontal };
        IReadOnlyList<int> counts = DivisionNames.UnitsAtEachVeterancy(rule);
        for (int level = 0; level < rule.VeterancyMultipliers.Count; level++)
        {
            int changed = level;
            var box = new EditBox(Share(rule.VeterancyMultipliers[level]), ShareWidth, typed =>
            {
                if (ReadShare(typed) is not double share) return false;
                List<double> shares = [.. rule.VeterancyMultipliers];
                shares[changed] = share;
                keep(rule with { VeterancyMultipliers = shares });
                return true;
            })
            {
                Margin = new Thickness(level == 0 ? 0 : 3, 0, 0, 0),
                ToolTip = level < counts.Count
                    ? Texts.Get("Divisions.Veterancy.Tip", ("level", Texts.Number(level + 1)), ("units", Texts.Number(counts[level])))
                    : Texts.Get("Divisions.Veterancy.Share.Tip", ("level", Texts.Number(level + 1))),
            };
            // a level not offered is a dim dash, as in the mock
            if (rule.VeterancyMultipliers[level] == 0) box.SetResourceReference(Control.ForegroundProperty, "muted");
            boxes.Children.Add(box);
        }
        return boxes;
    }

    /// <summary>A share as shown in its box: 0.68, with the language's decimal mark; "–" for a level not offered (0).</summary>
    private static string Share(double share) => share == 0 ? UnitNumbers.None : share.ToString("0.###", Texts.Catalog.Culture);

    /// <summary>
    /// A share typed in a box, from 0 to 1, with the language's decimal mark or a dot; a dash or nothing for 0 (the level
    /// not offered); null when it is none of these.
    /// </summary>
    private static double? ReadShare(string typed)
    {
        if (typed is "" or "-" or UnitNumbers.None) return 0;
        bool read = double.TryParse(typed, NumberStyles.Float, Texts.Catalog.Culture, out double share)
            || double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out share);
        return read && share >= 0 && share <= 1 ? share : null;
    }

    /// <summary>
    /// The units in one card, as a number to sort and draw a bar by; in Steel Division 2, which counts them by phase,
    /// the most of any phase.
    /// </summary>
    private static double? UnitsPerCard(DivisionUnit rule)
    {
        if (rule.UnitsPerCard is int perCard) return perCard;
        return rule.UnitsPerCardByPhase.Count > 0 ? rule.UnitsPerCardByPhase.Max() : null;
    }
}
