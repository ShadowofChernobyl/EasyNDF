using System.Globalization;
using System.Text.RegularExpressions;
using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// How Steel Division 2 keeps a weapon's values in its ammunition (Ammunition.ndf's TAmmunitionDescriptor), under its
/// own names and in its own forms (see the format notes, section 3.2), by the Armory's fields (<see cref="WeaponField"/>,
/// and the properties <see cref="WeaponFileValues"/> keeps under WARNO's names). Each property here is in every one of
/// the game's ammunition descriptors once at most, its hit roll's values too, so a rule's target needs no path. A value
/// WARNO has and Steel Division 2 does not (its traits, its projectile's speed and arc, whether it must deploy...) is
/// not here: the Armory does not write it for Steel Division 2.
/// </summary>
public static class SteelDivisionAmmunition
{
    /// <summary>How the game writes a value.</summary>
    public enum Form
    {
        /// <summary>A whole number: "1", "38".</summary>
        Whole,

        /// <summary>A number with a decimal point: "8.0", "2.5".</summary>
        Decimal,

        /// <summary>
        /// A distance, as ((N) * Metre), N in the files' own units: the Armory shows it in metres, as the game does
        /// (<see cref="Weapon.MetresScale"/>).
        /// </summary>
        Distance,

        /// <summary>A switch, a choice or a reference, written as chosen: "True", "EProjectileType/Obus", "~/Descriptor_Fire_Incendie".</summary>
        AsChosen,

        /// <summary>
        /// The level of the kind of damage, which the game writes inside it: AP 26 is "EArmeType/AP_26". Only an AP kind
        /// has a level (<see cref="HasLevel"/>).
        /// </summary>
        DamageLevel,
    }

    /// <summary>A property of the ammunition and how the game writes it.</summary>
    /// <param name="Name">Its name, as a rule's action targets it.</param>
    /// <param name="Form">How the game writes its value.</param>
    public sealed record Property(string Name, Form Form);

    private const string Separator = ActionApplier.PathSeparator;

    /// <summary>The game's kinds of damage, as an ammunition's Arme gives one: "EArmeType/AP_26".</summary>
    public const string DamageTypePrefix = "EArmeType/";

    /// <summary>The kind of damage whose types have levels: AP_1 to AP_60.</summary>
    public const string LevelledFamily = "AP";

    /// <summary>
    /// The properties, by the Armory's field. Checked against every ammunition of the game: each is found once in each
    /// descriptor that has it (only the descriptor's own DescriptorId and its hit roll's are two, and those are never
    /// written), in the form given here. The two chances to hit the game writes as whole numbers in most ammunition;
    /// the other two as "0.0" in all.
    /// </summary>
    private static readonly Dictionary<string, Property> ByField = new(StringComparer.Ordinal)
    {
        // damage
        [WeaponFileValues.DamageFamily] = new("Arme", Form.AsChosen),
        [WeaponField.DamageLevel] = new("Arme", Form.DamageLevel),
        [WeaponField.Damage] = new("PhysicalDamages", Form.Decimal),
        [WeaponField.Suppression] = new("SuppressDamages", Form.Decimal),
        [WeaponField.DamageRadius] = new("RadiusSplashPhysicalDamages", Form.Distance),
        [WeaponField.SuppressionRadius] = new("RadiusSplashSuppressDamages", Form.Distance),
        ["PiercingWeapon"] = new("PiercingWeapon", Form.AsChosen),
        ["DamageTypeEvolutionOverRangeDescriptor"] = new("DamageTypeEvolutionOverRangeDescriptor", Form.AsChosen),
        ["IsHarmlessForAllies"] = new("IsHarmlessForAllies", Form.AsChosen),
        ["FireTriggeringProbability"] = new("FireTriggeringProbability", Form.Decimal),
        ["FireDescriptor"] = new("FireDescriptor", Form.AsChosen),
        ["SmokeDescriptor"] = new("SmokeDescriptor", Form.AsChosen),
        ["IgnoreInflammabilityConditions"] = new("IgnoreInflammabilityConditions", Form.AsChosen),
        // range
        [WeaponField.Range] = new("PorteeMaximale", Form.Distance),
        [WeaponField.MinimumRange] = new("PorteeMinimale", Form.Distance),
        [WeaponField.HelicopterRange] = new("PorteeMaximaleHA", Form.Distance),
        ["MinimumRangeHelicopterGRU"] = new("PorteeMinimaleHA", Form.Distance),
        [WeaponField.PlaneRange] = new("PorteeMaximaleTBA", Form.Distance),
        ["MinimumRangeAirplaneGRU"] = new("PorteeMinimaleTBA", Form.Distance),
        ["MaximumRangeProjectileGRU"] = new("PorteeMaximaleProjectile", Form.Distance),
        ["MinimumRangeProjectileGRU"] = new("PorteeMinimaleProjectile", Form.Distance),
        // accuracy
        [WeaponField.AccuracyStopped] = new("BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Idling", Form.Whole),
        [WeaponField.AccuracyMoving] = new("BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Moving", Form.Whole),
        ["BaseCriticModifier"] = new("BaseCriticModifier", Form.Whole),
        [WeaponField.DispersionFar] = new("DispersionAtMaxRange", Form.Distance),
        [WeaponField.DispersionNear] = new("DispersionAtMinRange", Form.Distance),
        ["AngleDispersion"] = new("AngleDispersion", Form.Decimal),
        ["CorrectedShotDispersionMultiplier"] = new("CorrectedShotDispersionMultiplier", Form.Decimal),
        ["MaxSuccessiveHitCount"] = new("MaxSuccessiveHitCount", Form.Whole),
        [WeaponFileValues.AccuracyBase] = new(WeaponFileValues.AccuracyBase, Form.Decimal),
        [WeaponFileValues.AccuracyTargeted] = new(WeaponFileValues.AccuracyTargeted, Form.Decimal),
        // firing
        [WeaponField.AimingTime] = new("TempsDeVisee", Form.Decimal),
        [WeaponField.ShotsPerSalvo] = new("NbTirParSalves", Form.Whole),
        [WeaponFileValues.SimultaneousShots] = new("NbrProjectilesSimultanes", Form.Whole),
        ["TimeBetweenTwoShots"] = new("TempsEntreDeuxTirs", Form.Decimal),
        [WeaponField.Reload] = new("TempsEntreDeuxSalves", Form.Decimal),
        ["TimeBetweenTwoSalvos_Min"] = new("TempsEntreDeuxSalves_Min", Form.Decimal),
        ["TimeBetweenTwoSalvos_Max"] = new("TempsEntreDeuxSalves_Max", Form.Decimal),
        [WeaponField.SupplyCost] = new("SupplyCost", Form.Whole),
        ["AffichageMunitionParSalve"] = new("AffichageMunitionParSalve", Form.Whole),
        ["TimeBetweenTwoFx"] = new("TempsEntreDeuxFx", Form.Decimal),
        // use
        ["NbSalvosShootOnPosition"] = new("NbSalvosShootOnPosition", Form.Whole),
        ["TirIndirect"] = new("TirIndirect", Form.AsChosen),
        ["TirReflexe"] = new("TirReflexe", Form.AsChosen),
        // projectile
        ["ProjectileType"] = new("ProjectileType", Form.AsChosen),
        ["IsSubAmmunition"] = new("IsSubAmmunition", Form.AsChosen),
        ["MissileDescriptor"] = new("MissileDescriptor", Form.AsChosen),
        ["AffecteParNombre"] = new("AffecteParNombre", Form.AsChosen),
        // detection
        ["NoiseDissimulationMalus"] = new("NoiseDissimulationMalus", Form.Decimal),
        ["ShotsBeforeMaxNoise"] = new("ShotsBeforeMaxNoise", Form.Whole),
        // display
        [WeaponField.Picture] = new("InterfaceWeaponTexture", Form.AsChosen),
        ["MinMaxCategory"] = new("MinMaxCategory", Form.AsChosen),
        ["FxPower"] = new("FxPower", Form.AsChosen),
        ["WeaponCursorType"] = new("WeaponCursorType", Form.AsChosen),
    };

    /// <summary>The property a field is in, and how it is written; null for a value Steel Division 2's ammunition does not have.</summary>
    public static Property? Of(string field) => ByField.GetValueOrDefault(field);

    /// <summary>
    /// The values <see cref="WeaponFileValues"/> keeps under WARNO's names that Steel Division 2 keeps under others, by
    /// field: read from its own property into the field, so the pane shows them in the same place.
    /// </summary>
    public static IEnumerable<(string Field, Property Property)> RenamedFileValues =>
        WeaponFileValues.Names.Where(name => Of(name) is Property property && property.Name != name).Select(name => (name, Of(name)!));

    /// <summary>Whether a weapon's damage has a level the Armory can change: an AP kind, such as AP 26.</summary>
    public static bool HasLevel(Weapon weapon) => weapon.DamageFamily == LevelledFamily && weapon.DamageLevel > 0;

    /// <summary>
    /// The kind of damage a weapon has with another level, as the game writes it: AP 26 given 30 is "EArmeType/AP_30".
    /// Null for a weapon whose damage has no level (<see cref="HasLevel"/>).
    /// </summary>
    public static string? DamageTypeWithLevel(Weapon weapon, int level) =>
        HasLevel(weapon) ? DamageTypePrefix + LevelledFamily + "_" + level.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// A kind of damage as the Armory names it, family and level: "AP_14" (or "EArmeType/AP_14") is ("AP", 14),
    /// "Roquette" is ("Roquette", 0); one with more than a family and a level, such as "AP_1_Missile", is itself with no level.
    /// </summary>
    public static (string Family, int Level) DamageOf(string written)
    {
        string type = written.StartsWith(DamageTypePrefix, StringComparison.Ordinal) ? written[DamageTypePrefix.Length..] : written;
        Match parts = Regex.Match(type, "\\A([A-Za-z]+?)_?([0-9]*)\\z");
        if (!parts.Success) return (type, 0);
        return (parts.Groups[1].Value, parts.Groups[2].Value == "" ? 0 : int.Parse(parts.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A value as the game writes it for a property: whole, with a decimal point, as a distance in the files' own units
    /// (the metres shown times the weapon's <see cref="Weapon.MetresScale"/>), or as a kind of damage with the level.
    /// Null for a level the weapon's damage cannot have.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="value">The value, as the Armory shows it.</param>
    /// <param name="weapon">The weapon as the game has it: its scale, and its kind of damage.</param>
    public static string? Written(Property property, double value, Weapon weapon) => property.Form switch
    {
        Form.Whole => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
        Form.Distance => InFilesUnits(value * (weapon.MetresScale ?? 1)),
        Form.DamageLevel => DamageTypeWithLevel(weapon, (int)Math.Round(value)),
        _ => value.ToString("0.0##############", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// A distance as the game writes one: "((8750) * Metre)". Rounded to a millionth, so metres typed with decimals
    /// (25.4 m, 127 in the files) are not written with the computer's rounding error.
    /// </summary>
    private static string InFilesUnits(double number) =>
        "((" + Math.Round(number, 6).ToString("0.######", CultureInfo.InvariantCulture) + ") * Metre)";
}
