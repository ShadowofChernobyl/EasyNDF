using System.Globalization;

namespace EasyNdf.Data.Units;

/// <summary>
/// A stat the Armory lets the user change (in the New Unit window, and in the Unit pane), and what it takes: a number
/// of 0 or more, whole where the game writes it as one, and for an armour side a level its armour family allows.
/// </summary>
/// <param name="Field">Its field (<see cref="UnitField"/>).</param>
/// <param name="Read">Reads it from a unit; null when the unit has none.</param>
/// <param name="IsWhole">Whether the game writes it as a whole number.</param>
/// <param name="Family">For an armour side, its armour family among a unit's; null for other stats.</param>
public sealed record EditableStat(string Field, Func<Unit, double?> Read, bool IsWhole, Func<UnitArmorFamilies, string?>? Family = null)
{
    /// <summary>Every stat that can be changed, in the order the Unit pane shows them: its overview, then its armour.</summary>
    public static IReadOnlyList<EditableStat> All { get; } =
    [
        new(UnitField.Cost, unit => unit.Cost, IsWhole: true),
        new(UnitField.Strength, unit => unit.Strength, IsWhole: true),
        new(UnitField.Speed, unit => unit.Speed, IsWhole: true),
        new(UnitField.RoadSpeed, unit => unit.RoadSpeed, IsWhole: true),
        new(UnitField.Optics, unit => unit.Optics, IsWhole: false),
        new(UnitField.Stealth, unit => unit.Stealth, IsWhole: false),
        new(UnitField.Fuel, unit => unit.Fuel, IsWhole: true),
        new(UnitField.Vision, unit => unit.Vision, IsWhole: false),
        new(UnitField.FrontArmor, unit => unit.Armor.Front, IsWhole: true, families => families.Front),
        new(UnitField.SideArmor, unit => unit.Armor.Sides, IsWhole: true, families => families.Sides),
        new(UnitField.RearArmor, unit => unit.Armor.Rear, IsWhole: true, families => families.Rear),
        new(UnitField.TopArmor, unit => unit.Armor.Top, IsWhole: true, families => families.Top),
    ];

    /// <summary>The stat of a field; null for a field that cannot be changed.</summary>
    public static EditableStat? Of(string field) => All.FirstOrDefault(stat => stat.Field == field);

    /// <summary>The unit's armour family for this armour side; null for other stats, or when it is not known.</summary>
    public string? FamilyOf(Unit unit) => Family?.Invoke(unit.ArmorFamilies);

    /// <summary>
    /// A value typed for the stat of a unit, when the game takes it; null when it does not. Typed as the app writes
    /// numbers in its language, or with a "." for the decimals.
    /// </summary>
    /// <param name="text">What was typed.</param>
    /// <param name="unit">The unit it is for, whose armour families count.</param>
    /// <param name="levels">The levels of each armour family, from the game's DamageResistance.ndf; null when not known.</param>
    /// <param name="culture">How the app writes numbers in its language; null for English. Not this computer's own
    /// settings, which can differ: "2,5" typed in English must be refused, not read as 2.5 or 25.</param>
    public double? ValueOf(string text, Unit unit, IReadOnlyDictionary<string, int>? levels, CultureInfo? culture = null)
    {
        string trimmed = text.Trim();
        bool read = double.TryParse(trimmed, NumberStyles.Float, culture ?? CultureInfo.InvariantCulture, out double value)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (!read || value < 0 || double.IsInfinity(value) || double.IsNaN(value)) return null;
        if (IsWhole && (value != Math.Floor(value) || value > int.MaxValue)) return null;
        if (FamilyOf(unit) is string family && !ArmorLevels.Allows(levels, family, (int)value)) return null;
        return value;
    }
}
