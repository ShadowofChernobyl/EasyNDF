using System.Globalization;
using EasyNdf.Data.Units;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Weapons;

/// <summary>The names of a weapon's values that the Armory shows and changes, by which a changed value is found.</summary>
public static class WeaponField
{
    /// <summary>The name.</summary>
    public const string Name = "Name";

    /// <summary>The level of its kind of damage, such as 32 for AP 32.</summary>
    public const string DamageLevel = "DamageLevel";

    /// <summary>Its physical and suppression damage, and the radius each reaches.</summary>
    public const string Damage = "Damage", Suppression = "Suppression", DamageRadius = "DamageRadius", SuppressionRadius = "SuppressionRadius";

    /// <summary>Its ranges: at the ground, the least, at helicopters and at planes.</summary>
    public const string Range = "Range", MinimumRange = "MinimumRange", HelicopterRange = "HelicopterRange", PlaneRange = "PlaneRange";

    /// <summary>Its chance to hit when the unit is stopped and when it moves, and how far its shots scatter.</summary>
    public const string AccuracyStopped = "AccuracyStopped", AccuracyMoving = "AccuracyMoving", DispersionFar = "DispersionFar", DispersionNear = "DispersionNear";

    /// <summary>Its aiming time, the time between salvos, the shots a salvo and the supply a salvo costs.</summary>
    public const string AimingTime = "AimingTime", Reload = "Reload", ShotsPerSalvo = "ShotsPerSalvo", SupplyCost = "SupplyCost";

    /// <summary>The units that carry it.</summary>
    public const string Users = "Users";

    /// <summary>Its picture: the texture the game draws it with (InterfaceWeaponTexture).</summary>
    public const string Picture = "Picture";

    /// <summary>The trait icons the game shows beside it (TraitsToken).</summary>
    public const string Traits = "Traits";
}

/// <summary>
/// A value of a weapon the Armory lets the user change, and how: where the game keeps it in the ammunition (the target
/// of the rule that changes it), whether the game writes it as a whole number, and the highest it can be. Weapons
/// sharing an ammo box must fire the same number of shots a salvo: shots per salvo can be changed here all the same,
/// and the unit's loadout says when the weapons in a box no longer match.
/// </summary>
/// <param name="Field">Its field (<see cref="WeaponField"/>).</param>
/// <param name="Read">Reads it from a weapon; null when the weapon has none.</param>
/// <param name="Set">A weapon with the value in place of its own.</param>
/// <param name="Target">The property of the ammunition the value is in, as a rule's action names it.</param>
/// <param name="IsWhole">Whether the game writes it as a whole number.</param>
/// <param name="Highest">The highest it can be, such as 100 for a chance to hit; null for no limit.</param>
/// <param name="Lowest">The lowest it can be: 0 for most; -1 where the game writes -1 for "none".</param>
/// <param name="ShownTimes">What the pane shows it times: 1 for most; an angle the game keeps in radians is shown in degrees.</param>
public sealed record WeaponStat(string Field, Func<Weapon, double?> Read, Func<Weapon, double, Weapon> Set, string Target, bool IsWhole,
    double? Highest = null, double Lowest = 0, double ShownTimes = 1)
{
    private const string Separator = ActionApplier.PathSeparator;

    /// <summary>Every value that can be changed, in the order the Weapon pane shows them.</summary>
    public static IReadOnlyList<WeaponStat> All { get; } =
    [
        new(WeaponField.DamageLevel, weapon => weapon.DamageFamily == "" ? null : weapon.DamageLevel, (weapon, value) => weapon with { DamageLevel = (int)value },
            "Arme" + Separator + "Index", IsWhole: true),
        new(WeaponField.Damage, weapon => weapon.Damage, (weapon, value) => weapon with { Damage = value }, "PhysicalDamages", IsWhole: false),
        new(WeaponField.Suppression, weapon => weapon.Suppression, (weapon, value) => weapon with { Suppression = value }, "SuppressDamages", IsWhole: false),
        new(WeaponField.DamageRadius, weapon => weapon.DamageRadius, (weapon, value) => weapon with { DamageRadius = value }, "RadiusSplashPhysicalDamagesGRU", IsWhole: true),
        new(WeaponField.SuppressionRadius, weapon => weapon.SuppressionRadius, (weapon, value) => weapon with { SuppressionRadius = value }, "RadiusSplashSuppressDamagesGRU", IsWhole: true),
        new(WeaponField.Range, weapon => weapon.Range, (weapon, value) => weapon with { Range = value }, "MaximumRangeGRU", IsWhole: true),
        new(WeaponField.MinimumRange, weapon => weapon.MinimumRange, (weapon, value) => weapon with { MinimumRange = value }, "MinimumRangeGRU", IsWhole: true),
        new(WeaponField.HelicopterRange, weapon => weapon.HelicopterRange, (weapon, value) => weapon with { HelicopterRange = value }, "MaximumRangeHelicopterGRU", IsWhole: true),
        new(WeaponField.PlaneRange, weapon => weapon.PlaneRange, (weapon, value) => weapon with { PlaneRange = value }, "MaximumRangeAirplaneGRU", IsWhole: true),
        new(WeaponField.AccuracyStopped, weapon => weapon.AccuracyStopped, (weapon, value) => weapon with { AccuracyStopped = value },
            "BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Idling", IsWhole: true, Highest: 100),
        new(WeaponField.AccuracyMoving, weapon => weapon.AccuracyMoving, (weapon, value) => weapon with { AccuracyMoving = value },
            "BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Moving", IsWhole: true, Highest: 100),
        new(WeaponField.DispersionFar, weapon => weapon.DispersionFar, (weapon, value) => weapon with { DispersionFar = value }, "DispersionAtMaxRangeGRU", IsWhole: true),
        new(WeaponField.DispersionNear, weapon => weapon.DispersionNear, (weapon, value) => weapon with { DispersionNear = value }, "DispersionAtMinRangeGRU", IsWhole: true),
        new(WeaponField.AimingTime, weapon => weapon.AimingTime, (weapon, value) => weapon with { AimingTime = value }, "AimingTime", IsWhole: false),
        new(WeaponField.ShotsPerSalvo, weapon => weapon.ShotsPerSalvo, (weapon, value) => weapon with { ShotsPerSalvo = value }, "ShotsCountPerSalvo", IsWhole: true, Lowest: 1),
        new(WeaponField.Reload, weapon => weapon.Reload, (weapon, value) => weapon with { Reload = value }, "TimeBetweenTwoSalvos", IsWhole: false),
        new(WeaponField.SupplyCost, weapon => weapon.SupplyCost, (weapon, value) => weapon with { SupplyCost = value }, "SupplyCost", IsWhole: false),
        // the values kept as the file writes them (Weapon.FileValues); whole where every one the game has is written whole
        FromFile("MinimumRangeHelicopterGRU", isWhole: true),
        FromFile("MinimumRangeAirplaneGRU", isWhole: true),
        FromFile("MaximumRangeProjectileGRU", isWhole: true),
        FromFile("MinimumRangeProjectileGRU", isWhole: true),
        FromFile("FireTriggeringProbability", isWhole: false, highest: 1),
        FromFile("BaseCriticModifier", isWhole: true, highest: 100),
        FromFile("AngleDispersion", isWhole: false),
        FromFile("CorrectedShotDispersionMultiplier", isWhole: false),
        FromFile("MaxSuccessiveHitCount", isWhole: true),
        FromFile(WeaponFileValues.AccuracyBase, isWhole: true, highest: 100),
        FromFile(WeaponFileValues.AccuracyTargeted, isWhole: true, highest: 100),
        FromFile(WeaponFileValues.SimultaneousShots, isWhole: true, lowest: 1),
        FromFile("TimeBetweenTwoShots", isWhole: false),
        FromFile("TimeBetweenTwoSalvos_Min", isWhole: false, lowest: -1),
        FromFile("TimeBetweenTwoSalvos_Max", isWhole: false, lowest: -1),
        FromFile("AffichageMunitionParSalve", isWhole: true),
        FromFile("TimeBetweenTwoFx", isWhole: false),
        FromFile("NbSalvosShootOnPosition", isWhole: true),
        FromFile("ProjectileSpeedGRU", isWhole: false),
        FromFile("MaxAccelerationGRU", isWhole: true),
        FromFile(WeaponFileValues.Arc, isWhole: false, highest: Math.PI / 2, shownTimes: 180 / Math.PI),
        FromFile("MissileTimeBetweenCorrections", isWhole: false),
        FromFile("NoiseDissimulationMalus", isWhole: false),
        FromFile("ShotsBeforeMaxNoise", isWhole: true, lowest: 1),
    ];

    /// <summary>
    /// A value kept as the file writes it (<see cref="Weapon.FileValues"/>), changed by the property's own name, which
    /// is its field too.
    /// </summary>
    private static WeaponStat FromFile(string name, bool isWhole, double lowest = 0, double? highest = null, double shownTimes = 1) =>
        new(name, weapon => weapon.FileNumber(name), (weapon, value) => weapon.WithFileValue(name, WrittenAs(value, isWhole)), name, isWhole, highest, lowest, shownTimes);

    /// <summary>The value of a field; null for a field that cannot be changed.</summary>
    public static WeaponStat? Of(string field) => All.FirstOrDefault(stat => stat.Field == field);

    /// <summary>
    /// The key of a weapon's kind of damage among the game's levels (<see cref="UnitCatalog.ArmorLevels"/>, which
    /// holds the damage families' counts too): "DamageFamily_ap".
    /// </summary>
    public static string DamageFamilyKey(Weapon weapon) => "DamageFamily_" + weapon.DamageFamily;

    /// <summary>
    /// A value typed for this stat of a weapon, when the game takes it; null when it does not: a number no lower than
    /// <see cref="Lowest"/>, whole where the game writes one, no higher than <see cref="Highest"/>, for the damage level
    /// one its kind of damage has, and no more shots at once than a salvo has (nor fewer shots a salvo than it fires at
    /// once). Typed as the pane shows it (an angle in
    /// degrees), as the app writes numbers in its language or with a "." for the decimals.
    /// </summary>
    /// <param name="text">What was typed.</param>
    /// <param name="weapon">The weapon it is for.</param>
    /// <param name="levels">The levels of each damage family, from the game's DamageResistance.ndf; null when not known.</param>
    /// <param name="culture">How the app writes numbers in its language; null for English. Not this computer's own
    /// settings, which can differ: "2,5" typed in English must be refused, not read as 2.5 or 25.</param>
    public double? ValueOf(string text, Weapon weapon, IReadOnlyDictionary<string, int>? levels, CultureInfo? culture = null)
    {
        string trimmed = text.Trim();
        bool read = double.TryParse(trimmed, NumberStyles.Float, culture ?? CultureInfo.InvariantCulture, out double shown)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out shown);
        if (!read || double.IsInfinity(shown) || double.IsNaN(shown)) return null;
        double value = shown / ShownTimes;
        if (value < Lowest) return null;
        if (IsWhole && (value != Math.Floor(value) || value > int.MaxValue)) return null;
        if (Highest is double highest && value > highest) return null;
        if (Field == WeaponField.DamageLevel && !ArmorLevels.Allows(levels, DamageFamilyKey(weapon), (int)value)) return null;
        // shots fired at the same moment come out of one salvo
        if (Field == WeaponFileValues.SimultaneousShots && weapon.ShotsPerSalvo is double perSalvo && value > perSalvo) return null;
        if (Field == WeaponField.ShotsPerSalvo && weapon.FileNumber(WeaponFileValues.SimultaneousShots) is double atOnce && value < atOnce) return null;
        return value;
    }

    /// <summary>A value as the pane shows it: the angle in degrees, the others as they are.</summary>
    public double Shown(double value) => value * ShownTimes;

    /// <summary>A value as a rule writes it: a whole number as the game writes one, others with a decimal point, as the game does.</summary>
    public string Written(double value) => WrittenAs(value, IsWhole);

    private static string WrittenAs(double value, bool isWhole) => isWhole
        ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
        : value.ToString("0.0##############", CultureInfo.InvariantCulture);
}
