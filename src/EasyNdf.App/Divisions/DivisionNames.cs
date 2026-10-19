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
        var counts = new List<int>();
        foreach (double share in unit.VeterancyMultipliers)
        {
            if (share == 0) counts.Add(0);
            else counts.Add(Math.Max(1, (int)Math.Round(perCard * share, MidpointRounding.AwayFromZero)));
        }
        return counts;
    }

    /// <summary>The units at each veterancy level as a short text, "4 · 2 · 1 · –"; "–" when there are none.</summary>
    public static string VeterancyText(DivisionUnit unit)
    {
        IReadOnlyList<int> counts = UnitsAtEachVeterancy(unit);
        if (counts.Count == 0) return UnitNumbers.None;
        return string.Join(" · ", counts.Select(count => count == 0 ? UnitNumbers.None : Texts.Number(count)));
    }

    /// <summary>The units in one card: "4"; in Steel Division 2, by phase, "6 / 12 / 0".</summary>
    public static string UnitsPerCard(DivisionUnit unit) =>
        unit.UnitsPerCard is int perCard ? Texts.Number(perCard) : string.Join(" / ", unit.UnitsPerCardByPhase.Select(count => Texts.Number(count)));
}
