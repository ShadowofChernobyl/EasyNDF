using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Division pane beside or below the views: with a unit selected, how the division has it first; then the
/// division's emblem, name and what it is, and its details, activation-point costs and standout units, each changed in
/// place (see <see cref="DivisionDetailsEditor"/>).
/// </summary>
public static class DivisionPane
{
    /// <summary>How large the emblem is drawn.</summary>
    private const double EmblemSize = 64;

    /// <summary>The pane's content for a division, with the selected unit's part when it is one of the division's.</summary>
    /// <param name="division">The division shown.</param>
    /// <param name="selected">The selected unit and how the division has it; null for none.</param>
    /// <param name="units">The source's units by descriptor, to name transports and standout units.</param>
    /// <param name="game">The game the division is from.</param>
    /// <param name="editing">What changes the division, saved in the open mod.</param>
    public static FrameworkElement Build(Division division, DivisionEntry? selected, IReadOnlyDictionary<string, Unit> units, GameTitle game, IDivisionEditing editing)
    {
        var pane = new StackPanel { Margin = new Thickness(10, 0, 10, 10) };
        if (selected is not null) pane.Children.Add(UnitPane.Section(Texts.Get("Divisions.Pane.UnitInDivision", ("unit", selected.Unit.Name)), null, UnitRule(selected.Rule, units)));
        pane.Children.Add(Title(division, game));
        pane.Children.Add(UnitPane.Section(Texts.Get("Divisions.Pane.Division"), null, DivisionDetailsEditor.Details(division, units, editing)));
        pane.Children.Add(UnitPane.Section(Texts.Get("Divisions.Pane.Costs"), null, DivisionDetailsEditor.Costs(division, game, editing)));
        pane.Children.Add(UnitPane.Section(Texts.Get("Divisions.Pane.Standout"), null, DivisionDetailsEditor.StandoutUnits(division, units, editing)));
        return pane;
    }

    /// <summary>How the division has a unit: its cards, the units in each, at each veterancy, and its transports.</summary>
    private static Bevel UnitRule(DivisionUnit rule, IReadOnlyDictionary<string, Unit> units)
    {
        var pairs = new List<(string Name, string Value)>
        {
            (Texts.Get("Divisions.Column.Cards"), Texts.Number(rule.Cards)),
            (Texts.Get("Divisions.Column.PerCard"), DivisionNames.UnitsPerCard(rule)),
        };
        if (rule.UnitsPerCard is not null) pairs.Add((Texts.Get("Divisions.Column.Veterancy"), DivisionNames.VeterancyText(rule)));
        string transports = rule.Transports.Count == 0
            ? Texts.Get(rule.AvailableWithoutTransport ? "Divisions.NoTransportNeeded" : "Divisions.NoTransport")
            : string.Join(", ", rule.Transports.Select(transport => units.TryGetValue(transport, out Unit? unit) ? unit.Name : transport));
        pairs.Add((Texts.Get("Divisions.Column.Transports"), transports));
        return PairTable(pairs);
    }

    /// <summary>The emblem at the left; at its right the name, 14 pixels and bold, and under it the nation, side and type, dim.</summary>
    private static DockPanel Title(Division division, GameTitle game)
    {
        var name = new TextBlock { Text = division.Name, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        IEnumerable<string> parts = new[] { UnitNames.Nation(division.Country), UnitNames.Side(division.Side), DivisionNames.Type(division.TypeToken) }
            .Where(part => part != "");
        var what = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (division.Country != "")
            what.Children.Add(new NationMark(division.Country, division.Side == UnitNames.SidesOf(game)[0]) { Margin = new Thickness(0, 0, 5, 0) });
        what.Children.Add(UnitPane.Muted(string.Join(" · ", parts), new Thickness(0)));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, what } };
        var title = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var emblem = new DivisionEmblem(division.EmblemSource, EmblemSize, keepsRoom: false, division.EmblemFile) { Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(emblem, Dock.Left);
        title.Children.Add(emblem);
        title.Children.Add(words);
        return title;
    }

    /// <summary>Names and values, one pair a row, as the Unit pane's overview: the names dim on the panel colour.</summary>
    private static Bevel PairTable(IReadOnlyList<(string Name, string Value)> pairs)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < pairs.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool lastRow = row == pairs.Count - 1;
            grid.Children.Add(UnitPane.Cell(pairs[row].Name, row, 0, isName: true, lastRow));
            Bevel value = UnitPane.Cell(pairs[row].Value, row, 1, isName: false, lastRow);
            ((TextBlock)value.Child).TextWrapping = TextWrapping.Wrap;
            grid.Children.Add(value);
        }
        return UnitPane.Boxed(grid);
    }
}
