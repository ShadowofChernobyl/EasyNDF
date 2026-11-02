using System.Windows.Controls;
using EasyNdf.App.Units;
using EasyNdf.Data.Units;

namespace EasyNdf.App.Divisions;

/// <summary>
/// The place of a unit's tile among a division's cards: empty until it comes into view, when its tile is made
/// (<see cref="DivisionCards.MakeTilesInView"/>). A grid's cells are all the size of its biggest, so an empty place is
/// as big as the tiles made around it.
/// </summary>
public sealed class LazyTile : Border
{
    private readonly Func<UnitTile> make;

    /// <summary>A place for a unit's tile, made by <paramref name="make"/> when needed.</summary>
    public LazyTile(Unit unit, Func<UnitTile> make)
    {
        Unit = unit;
        this.make = make;
    }

    /// <summary>The unit whose tile goes here.</summary>
    public Unit Unit { get; }

    /// <summary>Whether its tile is made.</summary>
    public bool IsMade => Child is not null;

    /// <summary>The tile, once made; null before.</summary>
    public UnitTile? Tile => Child as UnitTile;

    /// <summary>Makes the tile, if it is not made yet.</summary>
    public void Make()
    {
        if (Child is null) Child = make();
    }
}
