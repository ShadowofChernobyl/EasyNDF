using System.Windows;
using System.Windows.Controls;
using EasyNdf.App.Controls;
using EasyNdf.App.Text;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Units;

/// <summary>
/// A line of the Cards view, which is a list of lines so only those on screen are made: a role's heading, or a row
/// of tiles.
/// </summary>
public abstract record CardLine;

/// <summary>A role's heading in the Cards view: ▾ or ▸, the role's name and how many units it has; a click folds it.</summary>
/// <param name="Role">The role's code ("" for units with none).</param>
/// <param name="Name">The role's name.</param>
/// <param name="Count">How many units shown have the role.</param>
/// <param name="IsFolded">Whether its tiles are hidden.</param>
public sealed record CardGroupLine(string Role, string Name, int Count, bool IsFolded) : CardLine
{
    /// <summary>▸ when folded, ▾ when open.</summary>
    public string Arrow => IsFolded ? "▸" : "▾";

    /// <summary>The count, as written.</summary>
    public string CountText => Texts.Number(Count);
}

/// <summary>A row of tiles in the Cards view: as many as fit across, each as wide as the others.</summary>
/// <param name="Units">The units of the row, at most <paramref name="Columns"/>.</param>
/// <param name="Columns">How many tiles fit across, so a short last row keeps the others' tile width.</param>
/// <param name="IsLast">Whether it is its role's last row, which keeps 8 pixels under it.</param>
/// <param name="Selection">Which units are selected (in the Add Units window, which are ticked).</param>
/// <param name="IsFirstSide">Whether a unit is on the game's first side (NATO, the Allies), for its nation's mark.</param>
/// <param name="Decorate">What else each tile shows where the row is (the Add Units window's tick box); null for nothing.</param>
public sealed record CardRowLine(IReadOnlyList<Unit> Units, int Columns, bool IsLast, UnitSelection Selection, Func<Unit, bool> IsFirstSide,
    Action<UnitTile>? Decorate = null) : CardLine;

/// <summary>What a list says while it has nothing to show, such as "No units match."</summary>
/// <param name="Text">The words.</param>
public sealed record EmptyLine(string Text) : CardLine;

/// <summary>A row of tiles: each tile in an equal share of the width, 8 pixels apart, 8 pixels in from the list's sides.</summary>
public sealed class CardRowView : Grid
{
    /// <summary>The space between tiles, and around them.</summary>
    public const double Gap = 8;

    /// <summary>The narrowest a tile is made before one fewer fits across.</summary>
    public const double SmallestTile = 138;

    /// <summary>How many tiles fit across a list this wide.</summary>
    public static int ColumnsFor(double width)
    {
        // the room inside the list's padding holds n tiles and the n - 1 gaps between them
        double inside = width - 2 * Gap;
        return Math.Max(1, (int)((inside + Gap) / (SmallestTile + Gap)));
    }

    /// <summary>Builds the row's tiles when the list gives it its line.</summary>
    public CardRowView()
    {
        DataContextChanged += (_, _) => Build();
    }

    /// <summary>
    /// Shows the row's line. The list reuses rows while it scrolls, and when a different number of tiles fits across:
    /// the row keeps its tiles and shows the new units in them, so neither makes new tiles.
    /// </summary>
    private void Build()
    {
        if (DataContext is not CardRowLine line)
        {
            Children.Clear();
            ColumnDefinitions.Clear();
            return;
        }
        Margin = new Thickness(Gap, Gap, Gap, line.IsLast ? Gap : 0);
        if (ColumnDefinitions.Count != 2 * line.Columns - 1)
        {
            ColumnDefinitions.Clear();
            for (int column = 0; column < line.Columns; column++)
            {
                if (column > 0) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
        }
        // the last row may have fewer units than columns
        while (Children.Count > line.Units.Count) Children.RemoveAt(Children.Count - 1);
        for (int at = 0; at < line.Units.Count; at++)
        {
            Unit unit = line.Units[at];
            bool firstSide = line.IsFirstSide(unit);
            if (at < Children.Count && Children[at] is UnitTile kept)
            {
                kept.Show(unit, firstSide);
                SetColumn(kept, at * 2);
                line.Decorate?.Invoke(kept);
                continue;
            }
            var tile = new UnitTile(unit, firstSide, line.Selection);
            SetColumn(tile, at * 2);
            Children.Insert(at, tile);
            line.Decorate?.Invoke(tile);
        }
    }
}
