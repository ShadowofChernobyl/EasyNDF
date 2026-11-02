namespace EasyNdf.Data.Divisions;

/// <summary>
/// The details of a division the Armory changes, by the names its rules keep the change under
/// (<see cref="DivisionChangeRules.ChangeKey"/>), and the parts a game may write in part only.
/// </summary>
public static class DivisionDetail
{
    /// <summary>Its name, nation (and side), type and tags.</summary>
    public const string Name = "Name", Nation = "Nation", Type = "Type", Tags = "Tags";

    /// <summary>Its activation points, and its place in the game's list of divisions.</summary>
    public const string Points = "Points", ListOrder = "ListOrder";

    /// <summary>Its summary and history.</summary>
    public const string Summary = "Summary", History = "History";

    /// <summary>The units shown on its card.</summary>
    public const string Standout = "Standout";

    /// <summary>Its emblem, one of the game's; and a picture of the user's own for it.</summary>
    public const string Emblem = "Emblem", EmblemPicture = "EmblemPicture";

    /// <summary>Its activation-point costs, each category's a change of its own ("Costs:Tanks").</summary>
    public const string Costs = "Costs";

    /// <summary>Its units: those added, taken out, or changed (cards, units per card, veterancy, transports).</summary>
    public const string Units = "Units";

    /// <summary>Taken out of the game's list of divisions.</summary>
    public const string TakenOut = "TakenOut";

    /// <summary>Its Official Battlegroup.</summary>
    public const string Battlegroup = "Battlegroup";

    /// <summary>A new division, made, duplicated or generated.</summary>
    public const string New = "New";

    /// <summary>Its phases (Steel Division 2): when each starts, its income and its bonus.</summary>
    public const string Phases = "Phases";

    /// <summary>Its income choices (Steel Division 2): one of the game's lists of them.</summary>
    public const string Economy = "Economy";

    /// <summary>A generated division (made again from the units at each export), a new division of its own kind.</summary>
    public const string Generated = "Generated";
}
