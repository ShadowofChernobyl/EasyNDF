using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// A weapon as the Armory's Weapons sub-tab shows it: one ammunition descriptor of the game (Ammunition.ndf, or
/// AmmunitionMissiles.ndf for WARNO's missiles). It is the weapon system itself (its range, damage, accuracy, reload,
/// picture) and is shared: every unit whose loadout mounts it uses this same one. A unit's loadout (its
/// WeaponDescriptor) only says which weapons it carries, where, and how many. A record, so a changed copy is made
/// with <c>with</c> (<see cref="WeaponStat.Set"/>).
/// </summary>
public sealed record Weapon
{
    /// <summary>Its descriptor, such as "Ammo_Canon_AP_120_mm_L52_M68".</summary>
    public required string Descriptor { get; init; }

    /// <summary>The file it is in: "Ammunition.ndf" or "AmmunitionMissiles.ndf".</summary>
    public required string File { get; init; }

    /// <summary>Its name, made from its descriptor as the Unit pane's weapons are: "Canon AP 120 mm L52 M68".</summary>
    public required string Name { get; init; }

    /// <summary>Its sort, as the weapon tables group them.</summary>
    public WeaponKind Kind { get; init; }

    /// <summary>Its category, as the Weapons sub-tab's type tabs group them (<see cref="WeaponCategories"/>).</summary>
    public WeaponCategory Category { get; init; }

    /// <summary>Its kind of damage, as the game names it: "ap", "he", "fmballe"...</summary>
    public string DamageFamily { get; init; } = "";

    /// <summary>The level of that damage, such as 32 for AP 32; 0 when the game gives none.</summary>
    public int DamageLevel { get; init; }

    /// <summary>Its physical damage, and the radius it reaches around the impact.</summary>
    public double? Damage { get; init; }

    /// <inheritdoc cref="Damage"/>
    public double? DamageRadius { get; init; }

    /// <summary>Its suppression damage, and the radius it reaches around the impact.</summary>
    public double? Suppression { get; init; }

    /// <inheritdoc cref="Suppression"/>
    public double? SuppressionRadius { get; init; }

    /// <summary>How far it shoots at ground targets, and the least distance it can, in the game's range unit.</summary>
    public double? Range { get; init; }

    /// <inheritdoc cref="Range"/>
    public double? MinimumRange { get; init; }

    /// <summary>How far it shoots at helicopters, and at planes; null or 0 when it cannot.</summary>
    public double? HelicopterRange { get; init; }

    /// <inheritdoc cref="HelicopterRange"/>
    public double? PlaneRange { get; init; }

    /// <summary>Its chance to hit, in percent, when the unit is stopped and when it moves.</summary>
    public double? AccuracyStopped { get; init; }

    /// <inheritdoc cref="AccuracyStopped"/>
    public double? AccuracyMoving { get; init; }

    /// <summary>How far its shots scatter at its longest and shortest range.</summary>
    public double? DispersionFar { get; init; }

    /// <inheritdoc cref="DispersionFar"/>
    public double? DispersionNear { get; init; }

    /// <summary>The seconds it takes to aim.</summary>
    public double? AimingTime { get; init; }

    /// <summary>The seconds between two salvos.</summary>
    public double? Reload { get; init; }

    /// <summary>How many shots one salvo fires.</summary>
    public double? ShotsPerSalvo { get; init; }

    /// <summary>What a salvo costs in supply.</summary>
    public double? SupplyCost { get; init; }

    /// <summary>Its traits as the game names them: "MOTION", "KINETIC", "HEAT", "F&amp;F"...</summary>
    public IReadOnlyList<string> Traits { get; init; } = [];

    /// <summary>Its picture's texture name, such as "Texture_Interface_Weapon_M68_120"; null when it has none.</summary>
    public string? PictureTexture { get; init; }

    /// <summary>Its picture's path in the game, such as "GameData:/Assets/2D/.../M68_120.png"; null when not found.</summary>
    public string? PictureSource { get; init; }

    /// <summary>The missile it fires (MissileDescriptors.ndf); null for a weapon that is not a missile.</summary>
    public string? Missile { get; init; }

    /// <summary>Whether it must be deployed to fire (HasDeploymentTime), as some artillery must.</summary>
    public bool MustDeploy { get; init; }

    /// <summary>
    /// The ammunition's other values the Weapon pane shows, exactly as its file writes them ("True", "1.0",
    /// "EProjectileType/Obus"), by the name a rule's action gives the property (<see cref="WeaponFileValues.Names"/>).
    /// A value the ammunition does not have is not here.
    /// </summary>
    public IReadOnlyDictionary<string, string> FileValues { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>One of <see cref="FileValues"/>, as written; null when the ammunition does not have it.</summary>
    public string? FileValue(string name) => FileValues.GetValueOrDefault(name);

    /// <summary>One of <see cref="FileValues"/> as a number; null when the ammunition does not have it, or it is not a number.</summary>
    public double? FileNumber(string name) =>
        FileValue(name) is string written && double.TryParse(written, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number : null;

    /// <summary>The same weapon with one of <see cref="FileValues"/> written as given.</summary>
    public Weapon WithFileValue(string name, string written) =>
        this with { FileValues = new Dictionary<string, string>(FileValues, StringComparer.Ordinal) { [name] = written } };

    /// <summary>The units that carry it, by descriptor, in the order the game lists them.</summary>
    public IReadOnlyList<string> Users { get; init; } = [];

    /// <summary>The same weapon with its users.</summary>
    public Weapon WithUsers(IReadOnlyList<string> users) => this with { Users = users };

    /// <summary>
    /// Each weapon with the units that carry it (<see cref="Users"/>), found from the units' weapons, in the order the
    /// units are listed.
    /// </summary>
    public static List<Weapon> WithUsersAmong(IEnumerable<Weapon> weapons, IEnumerable<Unit> units)
    {
        var users = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Unit unit in units)
        {
            foreach (string ammunition in unit.Weapons.Select(weapon => weapon.Ammunition).Distinct(StringComparer.Ordinal))
            {
                if (!users.TryGetValue(ammunition, out List<string>? list)) users[ammunition] = list = [];
                list.Add(unit.Descriptor);
            }
        }
        return [.. weapons.Select(weapon => weapon.WithUsers(users.GetValueOrDefault(weapon.Descriptor) ?? []))];
    }

    /// <summary>The weapon as a unit's list of weapons shows it, carried <paramref name="count"/> times.</summary>
    public UnitWeapon ForUnit(int count) =>
        new(Name, count, DamageFamily, DamageLevel, LongestRange, Damage, Suppression, Reload, Descriptor, File, Missile);

    /// <summary>Its longest range: at the ground, helicopters or planes.</summary>
    public double? LongestRange => new[] { Range, HelicopterRange, PlaneRange }.Where(range => range > 0).DefaultIfEmpty().Max();

    /// <summary>A weapon's name from its ammunition's descriptor: "Ammo_Canon_AP_120_mm_L52_M68" is "Canon AP 120 mm L52 M68".</summary>
    public static string NameOf(string descriptor) =>
        (descriptor.StartsWith("Ammo_", StringComparison.Ordinal) ? descriptor[5..] : descriptor).Replace('_', ' ');
}
