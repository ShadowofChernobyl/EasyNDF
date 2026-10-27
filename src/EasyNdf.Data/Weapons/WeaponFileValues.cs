using EasyNdf.Data.Units;
using EasyNdf.Engine.Ndf;
using EasyNdf.Engine.Rules;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Weapons;

/// <summary>
/// The ammunition's values a weapon keeps as its file writes them (<see cref="Weapon.FileValues"/>), for the Weapon
/// pane: those it has no property of its own for. Each is named as a rule's action names it, so the same name reads
/// it and changes it. Most are the ammunition's own properties, which a rule finds by name wherever they are (the
/// hit-roll values are inside its HitRollRuleDescriptor, and their names are found nowhere else in it); the kind of
/// damage and two chances to hit need a path.
/// </summary>
public static class WeaponFileValues
{
    private const string Separator = ActionApplier.PathSeparator;

    /// <summary>The kind of damage, inside its Arme: "DamageFamily_ap" in Arme = TDamageTypeRTTI(Family=DamageFamily_ap Index=32).</summary>
    public const string DamageFamily = "Arme" + Separator + "Family";

    /// <summary>The two chances to hit the game always gives 0: always, and when the target is targeted.</summary>
    public const string AccuracyBase = "BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Base",
        AccuracyTargeted = "BaseHitValueModifiers" + Separator + "EBaseHitValueModifier/Targeted";

    /// <summary>Whether it must be deployed to fire, which <see cref="Weapon.MustDeploy"/> also says.</summary>
    public const string HasDeploymentTime = "HasDeploymentTime";

    /// <summary>The shots it fires at the same moment, which can be no more than the shots a salvo.</summary>
    public const string SimultaneousShots = "SimultaneousShotsCount";

    /// <summary>The angle it fires its shells at, in radians, which the pane shows in degrees.</summary>
    public const string Arc = "PitchForParabolic";

    /// <summary>The ammunition's own properties kept, found by name.</summary>
    private static readonly string[] OwnNames =
    [
        // damage
        "AllowSuppressDamageWhenNoImpact", "PiercingWeapon", "DamageTypeEvolutionOverRangeDescriptor", "TandemCharge", "IsHarmlessForAllies",
        "FireTriggeringProbability", "FireDescriptor", "SmokeDescriptor", "ForceHitTopArmor", "ForceHitTopArmorOnSuccess",
        "ComputeArmorFromImpactLocation", "IgnoreInflammabilityConditions",
        // range
        "MinimumRangeHelicopterGRU", "MinimumRangeAirplaneGRU", "MaximumRangeProjectileGRU", "MinimumRangeProjectileGRU",
        // accuracy
        "DistanceToTarget", "BaseCriticModifier", "AngleDispersion", "CorrectedShotDispersionMultiplier", "MaxSuccessiveHitCount",
        // rate of fire
        SimultaneousShots, "TimeBetweenTwoShots", "TimeBetweenTwoSalvos_Min", "TimeBetweenTwoSalvos_Max", "AffichageMunitionParSalve", "TimeBetweenTwoFx",
        // use
        "CanShootWhileMoving", HasDeploymentTime, "CanShootOnPosition", "NbSalvosShootOnPosition", "TirIndirect", "TirReflexe",
        "ReflexShotDisabledIfPriorityTarget", "TargetOnlyOneUnitInDistrict",
        // projectile
        "ProjectileType", "ProjectileSpeedGRU", "MaxAccelerationGRU", Arc, "MissileTimeBetweenCorrections", "IsFireAndForget", "Guidance",
        "IsSubAmmunition", "SalvoShotsSorted", "AffecteParNombre",
        // detection
        "NoiseDissimulationMalus", "ShotsBeforeMaxNoise",
        // display
        "MinMaxCategory", "ShowDamageInUI", "DisplaySalveAccuracy", "FxPower", "ImpactHappening", "WeaponCursorType",
        // where it is in the game's files
        "DescriptorId", "Name", "TypeCategoryName", "Caliber", "WeaponDescriptionToken",
    ];

    /// <summary>Every value kept: the ammunition's own properties, then those that need a path.</summary>
    public static IEnumerable<string> Names => OwnNames.Concat([DamageFamily, AccuracyBase, AccuracyTargeted]);

    /// <summary>The values kept of an ammunition, as its file writes them; a value it does not have is left out.</summary>
    public static Dictionary<string, string> Read(ObjectValue ammunition)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in OwnNames)
            if (Written(Find(ammunition, name)) is string written) values[name] = written;
        if (Own(ammunition, "Arme") is ObjectValue damage && Written(Find(damage, "Family")) is string family) values[DamageFamily] = family;
        if (Find(ammunition, "BaseHitValueModifiers") is NdfValue chances)
        {
            if (NumberFor(chances, "Base") is double always) values[AccuracyBase] = Number(always);
            if (NumberFor(chances, "Targeted") is double targeted) values[AccuracyTargeted] = Number(targeted);
        }
        return values;
    }

    /// <summary>A single value as its file writes it: a number, a word, a text in its quotes, a reference; null for a list or an object.</summary>
    private static string? Written(NdfValue? value) => value switch
    {
        ScalarValue scalar => scalar.Text,
        WrappedValue { Kind: WrapKind.Negative, Inner: ScalarValue inner } => "-" + inner.Text,
        _ => null,
    };

    private static string Number(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
