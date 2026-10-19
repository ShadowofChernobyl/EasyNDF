using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The Divisions tab's Cards view: a division's units as the Units tab's tiles, under a heading for each category with
/// that category's slot costs at its right. Each tile says how many cards the division has of the unit, and how many
/// units in each. A click on a heading folds its tiles away, or brings them back, as on the Units tab.
/// </summary>
public static class DivisionCards
{
    /// <summary>The view for a division's groups, <paramref name="columns"/> tiles across.</summary>
    /// <param name="groups">The division's units by category.</param>
    /// <param name="game">The game the units are from.</param>
    /// <param name="selection">Which unit is selected.</param>
    /// <param name="columns">How many tiles across.</param>
    /// <param name="folded">The categories folded away, by code.</param>
    /// <param name="foldClicked">Folds a category away, or brings it back, when its heading is clicked.</param>
    public static StackPanel Build(IReadOnlyList<DivisionGroup> groups, GameTitle game, UnitSelection selection, int columns,
        IReadOnlySet<string> folded, Action<string> foldClicked)
    {
        var view = new StackPanel();
        string firstSide = UnitNames.SidesOf(game)[0];
        foreach (DivisionGroup group in groups)
        {
            bool isFolded = folded.Contains(group.Factory);
            Bevel heading = Heading(group, isFolded);
            string factory = group.Factory;
            heading.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                foldClicked(factory);
            };
            view.Children.Add(heading);
            if (isFolded) continue;
            var tiles = new UniformGrid { Columns = columns, Margin = new Thickness(CardRowView.Gap / 2) };
            foreach (DivisionEntry entry in group.Entries)
            {
                var tile = new UnitTile(entry.Unit, entry.Unit.Side == firstSide, selection) { Margin = new Thickness(CardRowView.Gap / 2) };
                tile.ShowDetail(
                    Texts.Get("Divisions.Card.Detail", ("cards", Texts.Number(entry.Rule.Cards)), ("units", DivisionNames.UnitsPerCard(entry.Rule))),
                    Texts.Get("Divisions.Card.Detail.Tip", ("cards", Texts.Number(entry.Rule.Cards)), ("units", DivisionNames.UnitsPerCard(entry.Rule))));
                tiles.Children.Add(tile);
            }
            view.Children.Add(tiles);
        }
        return view;
    }

    /// <summary>
    /// A category's heading, as the Units tab's role headings: ▾ (or ▸ when folded), its name in bold, its count dim, its
    /// slot costs at the right.
    /// </summary>
    private static Bevel Heading(DivisionGroup group, bool isFolded)
    {
        var arrow = new TextBlock { Text = isFolded ? "▸" : "▾", Width = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        DockPanel.SetDock(arrow, Dock.Left);
        var name = new TextBlock { Text = UnitNames.Factory(group.Factory), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Text = Texts.Number(group.Entries.Count), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "muted");
        var slots = new CostSlots(group.Costs);
        DockPanel.SetDock(slots, Dock.Right);
        var line = new DockPanel { LastChildFill = false, Children = { slots, arrow, name, count } };
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(count, Dock.Left);
        var heading = new Bevel { Child = line, Padding = new Thickness(8, 3, 8, 3), Edges = new Thickness(0, 1, 0, 1), Cursor = System.Windows.Input.Cursors.Hand };
        heading.SetResourceReference(Bevel.TopLeftProperty, "hi");
        heading.SetResourceReference(Bevel.BottomRightProperty, "lo");
        heading.SetResourceReference(Bevel.BackgroundProperty, "panel");
        return heading;
    }
}
