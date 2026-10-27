using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// Which transports a unit can be given in a division. A transport the game cannot load the unit into can break the
/// game when the unit is deployed, so only valid ones are ever offered.
/// </summary>
public static class Transports
{
    /// <summary>
    /// The transports a unit can ride in, in the order of the game's units. WARNO: those that can carry it (see
    /// <see cref="CanCarry"/>). Steel Division 2 does not follow those rules throughout, so there it is only the
    /// transports the game itself gives the unit in one of its divisions. None for a unit that cannot ride in one.
    /// </summary>
    public static IReadOnlyList<Unit> ValidFor(Unit unit, UnitCatalog catalog)
    {
        if (unit.SeatsTaken is null) return [];
        if (catalog.Game == GameTitle.Warno) return [.. catalog.Units.Where(transport => CanCarry(transport, unit))];
        HashSet<string> givenByGame = [.. catalog.Divisions.SelectMany(division => division.Units)
            .Where(rule => rule.Unit == unit.Descriptor)
            .SelectMany(rule => rule.Transports)];
        return [.. catalog.Units.Where(transport => givenByGame.Contains(transport.Descriptor))];
    }

    /// <summary>
    /// Whether a transport can carry a unit, as WARNO decides it; every transport WARNO's own divisions give a unit
    /// passes all five:
    /// <list type="bullet">
    /// <item>the unit can ride in a transport (it has a TTransportableModuleDescriptor);</item>
    /// <item>the transport carries one of the unit's tags (its TransportableTagSet against the unit's TagSet);</item>
    /// <item>the unit's seats fit in the transport's;</item>
    /// <item>both are on the same side (the game pairs nations, but never sides);</item>
    /// <item>the transport has a deck-code number, without which no deck can hold it.</item>
    /// </list>
    /// </summary>
    public static bool CanCarry(Unit transport, Unit unit)
    {
        if (unit.SeatsTaken is not double seatsTaken) return false;
        if (transport.CarriesTags is not { } carries || transport.Seats is not double seats) return false;
        bool sharesATag = carries.Any(tag => unit.Tags.Contains(tag, StringComparer.Ordinal));
        return sharesATag && seatsTaken <= seats && transport.Side == unit.Side && transport.HasDeckNumber;
    }
}
