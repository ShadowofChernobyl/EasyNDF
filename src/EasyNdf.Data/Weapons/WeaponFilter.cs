using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The filters of the Armory's Weapons sub-tab: what is typed (in a weapon's name or descriptor), the side and the
/// nations of the units that carry it, its category (the type tab chosen), its kind of damage, whether it is carried
/// at all, and whether it is modded (not one of the vanilla game's). A weapon's side and nations are those of the units carrying it, as a weapon has none of its own.
/// </summary>
public sealed class WeaponFilter
{
    /// <summary>The text typed; empty for every weapon.</summary>
    public string Text { get; set; } = "";

    /// <summary>The side the units carrying it are on, such as "NATO"; empty for both.</summary>
    public string Side { get; set; } = "";

    /// <summary>The nations ticked, by code; none for every nation.</summary>
    public HashSet<string> Nations { get; } = [];

    /// <summary>The category chosen with the type tabs; null for every category.</summary>
    public WeaponCategory? Category { get; set; }

    /// <summary>The kind of damage chosen, such as "ap"; empty for every kind.</summary>
    public string DamageFamily { get; set; } = "";

    /// <summary>Whether only weapons some unit carries are shown.</summary>
    public bool OnlyInUse { get; set; }

    /// <summary>Only modded weapons: those not in <see cref="VanillaWeapons"/>, as the Units sub-tab's "Only modded units".</summary>
    public bool OnlyModded { get; set; }

    /// <summary>The descriptors of vanilla WARNO's (or Steel Division 2's) weapons, before any mod; a weapon not among them is modded.</summary>
    public IReadOnlySet<string> VanillaWeapons { get; set; } = new HashSet<string>();

    /// <summary>The weapons the filters leave, in the order given.</summary>
    /// <param name="weapons">Every weapon.</param>
    /// <param name="units">The units, by descriptor, for the sides and nations of those that carry each weapon.</param>
    public List<Weapon> Apply(IEnumerable<Weapon> weapons, IReadOnlyDictionary<string, Unit> units) =>
        [.. weapons.Where(weapon => Keeps(weapon, units, checkCategory: true))];

    /// <summary>How many weapons each category's tab would show, with every other filter as it is.</summary>
    public Dictionary<WeaponCategory, int> CategoryCounts(IEnumerable<Weapon> weapons, IReadOnlyDictionary<string, Unit> units)
    {
        var counts = new Dictionary<WeaponCategory, int>();
        foreach (Weapon weapon in weapons.Where(weapon => Keeps(weapon, units, checkCategory: false)))
            counts[weapon.Category] = counts.GetValueOrDefault(weapon.Category) + 1;
        return counts;
    }

    /// <summary>How many weapons units of each nation carry, with the side and the other filters as they are, but no nation ticked.</summary>
    public Dictionary<string, int> NationCounts(IEnumerable<Weapon> weapons, IReadOnlyDictionary<string, Unit> units)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Weapon weapon in weapons.Where(weapon => Keeps(weapon, units, checkCategory: true, checkNations: false)))
            foreach (string nation in CarriersOf(weapon, units).Select(unit => unit.Nation).Distinct(StringComparer.Ordinal))
                counts[nation] = counts.GetValueOrDefault(nation) + 1;
        return counts;
    }

    private bool Keeps(Weapon weapon, IReadOnlyDictionary<string, Unit> units, bool checkCategory, bool checkNations = true)
    {
        if (checkCategory && Category is WeaponCategory category && weapon.Category != category) return false;
        if (DamageFamily != "" && !string.Equals(weapon.DamageFamily, DamageFamily, StringComparison.Ordinal)) return false;
        if (OnlyInUse && weapon.Users.Count == 0) return false;
        if (OnlyModded && VanillaWeapons.Contains(weapon.Descriptor)) return false;
        if (!MatchesText(weapon)) return false;
        bool needsCarriers = Side != "" || (checkNations && Nations.Count > 0);
        if (!needsCarriers) return true;
        // carried by at least one unit that is on the side, and of a nation ticked
        return CarriersOf(weapon, units).Any(unit =>
            (Side == "" || unit.Side == Side) && (!checkNations || Nations.Count == 0 || Nations.Contains(unit.Nation)));
    }

    /// <summary>Whether the weapon's name or descriptor has the text typed, in any case.</summary>
    private bool MatchesText(Weapon weapon)
    {
        string typed = Text.Trim();
        if (typed == "") return true;
        return weapon.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || weapon.Descriptor.Contains(typed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The units that carry a weapon, those the source has.</summary>
    private static IEnumerable<Unit> CarriersOf(Weapon weapon, IReadOnlyDictionary<string, Unit> units) =>
        weapon.Users.Select(units.GetValueOrDefault).OfType<Unit>();
}
