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
    /// Every switch and choice that can be changed, in the order the Weapon pane shows them, those whose effect is not
    /// known too (the pane marks them ?). A reference to another descriptor (its missile, its fire, its smoke) is a
    /// choice among those the game's weapons name, so it is never one the game does not have.
    /// </summary>
    public static IReadOnlyList<WeaponChoice> All { get; } =
    [
        new(WeaponFileValues.DamageFamily, IsSwitch: false),
        new("AllowSuppressDamageWhenNoImpact", IsSwitch: true),
        new("PiercingWeapon", IsSwitch: true),
        new("DamageTypeEvolutionOverRangeDescriptor", IsSwitch: false),
        new("TandemCharge", IsSwitch: true),
        new("IsHarmlessForAllies", IsSwitch: true),
        new("FireDescriptor", IsSwitch: false),
        new("SmokeDescriptor", IsSwitch: false),
        new("ForceHitTopArmor", IsSwitch: true),
        new("ForceHitTopArmorOnSuccess", IsSwitch: true),
        new("ComputeArmorFromImpactLocation", IsSwitch: true),
        new("IgnoreInflammabilityConditions", IsSwitch: true),
        new("DistanceToTarget", IsSwitch: true),
        new("CanShootWhileMoving", IsSwitch: true),
        new(WeaponFileValues.HasDeploymentTime, IsSwitch: true),
        new("CanShootOnPosition", IsSwitch: true),
        new("TirIndirect", IsSwitch: true),
        new("TirReflexe", IsSwitch: true),
        new("ReflexShotDisabledIfPriorityTarget", IsSwitch: true),
        new("TargetOnlyOneUnitInDistrict", IsSwitch: true),
        new("ProjectileType", IsSwitch: false),
        new("IsFireAndForget", IsSwitch: true),
        new("Guidance", IsSwitch: false),
        new("IsSubAmmunition", IsSwitch: true),
        new("MissileDescriptor", IsSwitch: false),
        new("SalvoShotsSorted", IsSwitch: true),
        new("AffecteParNombre", IsSwitch: true),
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
    /// A weapon with the value chosen in place of its own. The kind of damage, whether it must be deployed and the missile
    /// it fires are also the weapon's own properties, which other parts of the Armory read, so they change with it.
    /// Steel Division 2's kind of damage has its level in it ("EArmeType/AP_26"), so the level changes with it too.
    /// </summary>
    public Weapon Set(Weapon weapon, string written)
    {
        Weapon changed = weapon.WithFileValue(Field, written);
        if (Field == WeaponFileValues.DamageFamily && written.StartsWith(SteelDivisionAmmunition.DamageTypePrefix, StringComparison.Ordinal))
        {
            (string family, int level) = SteelDivisionAmmunition.DamageOf(written);
            return changed with { DamageFamily = family, DamageLevel = level };
        }
        if (Field == WeaponFileValues.DamageFamily) return changed with { DamageFamily = FamilyName(written) };
        if (Field == WeaponFileValues.HasDeploymentTime) return changed with { MustDeploy = written == On };
        if (Field == "MissileDescriptor") return changed with { Missile = written == "nil" ? null : written };
        return changed;
    }

    /// <summary>
    /// The values a weapon can be given: for a switch, on and off; for a choice, each value the game's weapons have,
    /// in order, numbers by their value (AP_4 before AP_38). A kind of damage is offered only when it has the weapon's
    /// level, as the game takes no other.
    /// </summary>
    /// <param name="weapon">The weapon.</param>
    /// <param name="weapons">The game's weapons, whose values are offered.</param>
    /// <param name="levels">The levels of each damage family, from the game's DamageResistance.ndf; null when not known.</param>
    public IReadOnlyList<string> Offered(Weapon weapon, IEnumerable<Weapon> weapons, IReadOnlyDictionary<string, int>? levels)
    {
        if (IsSwitch) return [On, Off];
        var values = new SortedSet<string>(NumbersInOrder.Instance);
        foreach (Weapon each in weapons)
            if (Read(each) is string written) values.Add(written);
        if (Read(weapon) is string own) values.Add(own);
        if (Field != WeaponFileValues.DamageFamily) return [.. values];
        return [.. values.Where(family => family == Read(weapon) || ArmorLevels.Allows(levels, family, weapon.DamageLevel))];
    }

    /// <summary>
    /// A kind of damage as the Armory names it: WARNO's "DamageFamily_ap" is "ap"; Steel Division 2's
    /// "EArmeType/AP_26", with its level, is "AP_26".
    /// </summary>
    public static string FamilyName(string written)
    {
        foreach (string prefix in new[] { "DamageFamily_", SteelDivisionAmmunition.DamageTypePrefix })
            if (written.StartsWith(prefix, StringComparison.Ordinal)) return written[prefix.Length..];
        return written;
    }
}

/// <summary>
/// Orders texts as a person would, in any case and with the numbers in them by their value: Steel Division 2's kinds of
/// damage "AP_4", "AP_38", "AP_40", where text order puts AP_38 before AP_4.
/// </summary>
internal sealed class NumbersInOrder : IComparer<string>
{
    /// <summary>The one comparer.</summary>
    public static NumbersInOrder Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                // leading zeros left out, a longer run of digits is the larger number
                string a = x[startX..i].TrimStart('0'), b = y[startY..j].TrimStart('0');
                int byValue = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (byValue != 0) return byValue;
                continue;
            }
            int byLetter = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byLetter != 0) return byLetter;
            i++;
            j++;
        }
        // texts the same but for case are one, as before
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
