using System.Text.Json.Nodes;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Divisions;

/// <summary>
/// The fixed state a new division starts in, whichever division or source it is made from: no emblem (an empty
/// placeholder), a placeholder type in WARNO (none in Steel Division 2), no standout units, last in its nation's
/// list, and the game's usual activation points and card-slot costs. A new division made from a modded source would
/// otherwise take that mod's numbers (9,999 points, dozens of slots) and the Armory would not draw it well.
/// </summary>
public static class NewDivisionStart
{
    /// <summary>
    /// The type a new WARNO division is given until one is chosen: Mechanized Infantry, the game's most common. WARNO
    /// shuts down when its list of battlegroups shows a division with no type ("Division token 'INVALID!' is not
    /// linked to any texture"), as every type must have a picture in DivisionTypes.ndf and an empty one has none.
    /// </summary>
    public const string WarnoPlaceholderType = "MECHANIZ";

    /// <summary>
    /// WARNO's usual activation points: every one of its 70 multiplayer divisions has 50 (the game's files of
    /// October 2026). The divisions with 9,999 are not for multiplayer.
    /// </summary>
    public const int WarnoActivationPoints = 50;

    /// <summary>
    /// WARNO's usual card-slot costs for each category: for each slot, the middle value of the 70 multiplayer
    /// divisions' costs, with as many slots as most of them have, rising slot by slot (the game's files of October
    /// 2026). Most divisions have no Defense slots.
    /// </summary>
    public static Dictionary<string, List<int>> WarnoCosts() => new(StringComparer.Ordinal)
    {
        ["Logistic"] = [1, 1, 2, 2, 3, 3, 3],
        ["Infantry"] = [1, 1, 1, 1, 1, 1, 1, 1, 2, 2],
        ["Art"] = [1, 1, 1, 2, 3, 3, 3],
        ["Tanks"] = [1, 1, 1, 2, 2, 3, 3],
        ["Recons"] = [1, 1, 1, 2, 3, 3, 3],
        ["DCA"] = [1, 1, 2, 2, 3, 3],
        ["Helis"] = [1, 1, 2, 3],
        ["Planes"] = [1, 1, 2, 2, 3, 3, 3],
        ["Defense"] = [],
    };

    /// <summary>
    /// The next deck number free: one more than the highest the game's divisions and the mod's new ones have. The
    /// game's numbers are under a thousand, so this keeps to the range it uses itself.
    /// </summary>
    public static int NextDeckNumber(IReadOnlyList<Division> game, Mod mod)
    {
        int highest = game.Select(division => division.DeckNumber ?? 0).Concat(mod.Divisions.Select(division => division.DeckNumber ?? 0)).DefaultIfEmpty(0).Max();
        return highest + 1;
    }

    /// <summary>
    /// Gives a new division its starting state. Steel Division 2's divisions are laid out differently and are not
    /// edited yet, so there only the emblem, type, standout units and place in the list are set; its points and costs
    /// stay those of the division it is made from.
    /// </summary>
    /// <param name="created">The new division.</param>
    /// <param name="game">The game's divisions, to put it after the last of them.</param>
    /// <param name="title">The game.</param>
    /// <param name="mod">The mod, whose other new divisions' deck numbers are taken too.</param>
    public static void Give(ModDivision created, IReadOnlyList<Division> game, Mod mod, GameTitle title)
    {
        // "" is the empty placeholder: no picture, rather than the starting division's
        created.EmblemTexture = "";
        created.EmblemSource = "";
        created.TypeToken = "";
        created.StandoutUnits = [];
        created.ListOrder = (game.Select(division => division.ListOrder).Max() ?? 0) + 1;
        created.DeckNumber = NextDeckNumber(game, mod);
        if (title != GameTitle.Warno) return;
        created.TypeToken = WarnoPlaceholderType;
        created.ActivationPoints = WarnoActivationPoints;
        created.Costs = WarnoCosts();
    }

    /// <summary>
    /// Gives each of the mod's new WARNO divisions that has no type the placeholder type (<see cref="WarnoPlaceholderType"/>),
    /// in its details and in the rules it is exported with, so that the Armory shows the type the game is given. New
    /// divisions were made without one until October 2026. A new WARNO division is told by its card-slot costs, which
    /// only WARNO's are given (<see cref="Give"/>); a Steel Division 2 division is left as it is.
    /// </summary>
    /// <returns>Whether a division was given one.</returns>
    public static bool GivePlaceholderTypes(Mod mod)
    {
        bool given = false;
        foreach (ModDivision division in mod.Divisions.Where(division => division.IsNew && division.TypeToken == "" && division.Costs is not null))
        {
            division.TypeToken = WarnoPlaceholderType;
            foreach (JsonObject action in TypeActionsOf(mod, division.Descriptor).Where(action => JsonValues.AsText(action["Value"]) == "\"\""))
                action["Value"] = "\"" + WarnoPlaceholderType + "\"";
            given = true;
        }
        return given;
    }

    /// <summary>The actions setting a division's type (TypeToken) in the rules the Armory made for it.</summary>
    private static IEnumerable<JsonObject> TypeActionsOf(Mod mod, string descriptor) =>
        ArmoryPresets.RulesOf(mod).OfType<JsonObject>()
            .Where(rule => JsonValues.AsText(rule["Armory"]?["Division"]) == descriptor)
            .SelectMany(rule => (rule["Actions"] as JsonArray ?? []).OfType<JsonObject>())
            .Where(action => JsonValues.AsText(action["Target"]) == "TypeToken");
}
