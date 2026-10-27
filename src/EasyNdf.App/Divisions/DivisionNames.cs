using EasyNdf.App.Text;
using EasyNdf.App.Units;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Divisions;

/// <summary>How the Divisions tab names and counts what the game writes as codes and lists.</summary>
public static class DivisionNames
{
    /// <summary>WARNO's division types, by their key (TypeToken), with the app's text for each.</summary>
    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["ARMORED"] = "Divisions.Type.Armored",
        ["MECHANIZ"] = "Divisions.Type.Mechanized",
        ["MOTORIZD"] = "Divisions.Type.Motorized",
        ["AIRBORNE"] = "Divisions.Type.Airborne",
        ["AIRMOBIL"] = "Divisions.Type.Airmobile",
        ["DIVNAVAL"] = "Divisions.Type.Naval",
        ["INFANREG"] = "Divisions.Type.Territorial",
        ["ARMORREC"] = "Divisions.Type.Cavalry",
    };

    /// <summary>A division's type: "ARMORED" is "Armored"; a key the app does not know is shown as it is; empty for none.</summary>
    public static string Type(string token) => Types.TryGetValue(token, out string? key) ? Texts.Get(key) : token;

    /// <summary>
    /// The units in one card at each veterancy level, from the lowest: the units per card times that level's share,
    /// at least one, and 0 where the level is not offered. Empty when the game counts units by phase instead (Steel
    /// Division 2).
    /// </summary>
    public static IReadOnlyList<int> UnitsAtEachVeterancy(DivisionUnit unit)
    {
        if (unit.UnitsPerCard is not int perCard) return [];
        return [.. unit.VeterancyMultipliers.Select(share => UnitsAt(perCard, share))];
    }

    /// <summary>The units in one card at a level with this share: the units per card times it, at least one; 0 for a level not offered.</summary>
    public static int UnitsAt(int unitsPerCard, double share) =>
        share == 0 ? 0 : Math.Max(1, (int)Math.Round(unitsPerCard * share, MidpointRounding.AwayFromZero));

    /// <summary>The units in one card: "4"; in Steel Division 2, by phase, "6 / 12 / 0".</summary>
    public static string UnitsPerCard(DivisionUnit unit) =>
        unit.UnitsPerCard is int perCard ? Texts.Number(perCard) : string.Join(" / ", unit.UnitsPerCardByPhase.Select(count => Texts.Number(count)));
}
