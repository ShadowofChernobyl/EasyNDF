using EasyNdf.Data.Units;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// A value of a weapon the Armory lets the user change that is not a number: a switch (True or False) or a choice
/// among the values the game's weapons have (a kind of projectile, an effect, a cursor...). Each is kept as its file
/// writes it (<see cref="Weapon.FileValues"/>), and a rule writes the one chosen as it is, so nothing new to the game
/// is ever written. Its field is the property's name, as a rule's action gives it.
/// </summary>
/// <param name="Field">The property's name, as a rule's action gives it (<see cref="WeaponFileValues"/>).</param>
/// <param name="IsSwitch">Whether it is a switch, True or False; otherwise a choice.</param>
public sealed record WeaponChoice(string Field, bool IsSwitch)
{
    /// <summary>A switch on, and off, as the game writes them.</summary>
    public const string On = "True", Off = "False";

    /// <summary>
    /// Every switch and choice that can be changed, in the order the Weapon pane shows them. Those whose effect is not
    /// known are left out: the pane shows them, but they are not changed until tried in the game.
    /// </summary>
    public static IReadOnlyList<WeaponChoice> All { get; } =
    [
        new(WeaponFileValues.DamageFamily, IsSwitch: false),
        new("AllowSuppressDamageWhenNoImpact", IsSwitch: true),
        new("PiercingWeapon", IsSwitch: true),
        new("TandemCharge", IsSwitch: true),
        new("IsHarmlessForAllies", IsSwitch: true),
        new("ForceHitTopArmor", IsSwitch: true),
        new("ForceHitTopArmorOnSuccess", IsSwitch: true),
        new("ComputeArmorFromImpactLocation", IsSwitch: true),
        new("DistanceToTarget", IsSwitch: true),
        new("CanShootWhileMoving", IsSwitch: true),
        new(WeaponFileValues.HasDeploymentTime, IsSwitch: true),
        new("CanShootOnPosition", IsSwitch: true),
        new("TirIndirect", IsSwitch: true),
        new("ProjectileType", IsSwitch: false),
        new("IsFireAndForget", IsSwitch: true),
        new("Guidance", IsSwitch: false),
        new("IsSubAmmunition", IsSwitch: true),
        new("MinMaxCategory", IsSwitch: false),
        new("ShowDamageInUI", IsSwitch: true),
        new("DisplaySalveAccuracy", IsSwitch: true),
        new("FxPower", IsSwitch: false),
        new("ImpactHappening", IsSwitch: false),
        new("WeaponCursorType", IsSwitch: false),
    ];

    /// <summary>The switch or choice of a field; null for a field that is neither, or cannot be changed.</summary>
    public static WeaponChoice? Of(string field) => All.FirstOrDefault(choice => choice.Field == field);

    /// <summary>The value a weapon has, as its file writes it; null when its ammunition does not have it, and a rule cannot set it.</summary>
    public string? Read(Weapon weapon) => weapon.FileValue(Field);

    /// <summary>
    /// A weapon with the value chosen in place of its own. The kind of damage and whether it must be deployed are also
    /// the weapon's own properties, which other parts of the Armory read, so they change with it.
    /// </summary>
    public Weapon Set(Weapon weapon, string written)
    {
        Weapon changed = weapon.WithFileValue(Field, written);
        if (Field == WeaponFileValues.DamageFamily) return changed with { DamageFamily = FamilyName(written) };
        if (Field == WeaponFileValues.HasDeploymentTime) return changed with { MustDeploy = written == On };
        return changed;
    }

    /// <summary>
    /// The values a weapon can be given: for a switch, on and off; for a choice, each value the game's weapons have,
    /// in order. A kind of damage is offered only when it has the weapon's level, as the game takes no other.
    /// </summary>
    /// <param name="weapon">The weapon.</param>
    /// <param name="weapons">The game's weapons, whose values are offered.</param>
    /// <param name="levels">The levels of each damage family, from the game's DamageResistance.ndf; null when not known.</param>
    public IReadOnlyList<string> Offered(Weapon weapon, IEnumerable<Weapon> weapons, IReadOnlyDictionary<string, int>? levels)
    {
        if (IsSwitch) return [On, Off];
        var values = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Weapon each in weapons)
            if (Read(each) is string written) values.Add(written);
        if (Read(weapon) is string own) values.Add(own);
        if (Field != WeaponFileValues.DamageFamily) return [.. values];
        return [.. values.Where(family => family == Read(weapon) || ArmorLevels.Allows(levels, family, weapon.DamageLevel))];
    }

    /// <summary>A kind of damage as the Armory names it: "DamageFamily_ap" is "ap".</summary>
    public static string FamilyName(string written)
    {
        const string Prefix = "DamageFamily_";
        return written.StartsWith(Prefix, StringComparison.Ordinal) ? written[Prefix.Length..] : written;
    }
}
