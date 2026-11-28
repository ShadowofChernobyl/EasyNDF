using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;
using EasyNdf.Engine.Ndf;
using static EasyNdf.Data.Units.NdfValues;

namespace EasyNdf.Data.Tests;

/// <summary>
/// An ammunition's file values are read with its properties gone through once, not once for each name looked for:
/// every value must still be the first property of its name, outer objects before those inside them, in written order
/// (<see cref="NdfValues.Find"/>), as <see cref="ReadBefore"/>, the method as it was, gives it.
/// </summary>
public class WeaponFileValuesSpeedTests
{
    /// <summary><see cref="WeaponFileValues.Read"/> as it was before it was made faster.</summary>
    private static Dictionary<string, string> ReadBefore(ObjectValue ammunition, Func<double?, double?>? inMetres = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] ownNames = [.. WeaponFileValues.Names.Except([WeaponFileValues.DamageFamily, WeaponFileValues.AccuracyBase, WeaponFileValues.AccuracyTargeted])];
        foreach (string name in ownNames)
            if (Written(Find(ammunition, name)) is string written) values[name] = written;
        if (Own(ammunition, "Arme") is ObjectValue damage && Written(Find(damage, "Family")) is string family) values[WeaponFileValues.DamageFamily] = family;
        foreach ((string field, SteelDivisionAmmunition.Property property) in SteelDivisionAmmunition.RenamedFileValues)
        {
            if (values.ContainsKey(field) || Find(ammunition, property.Name) is not NdfValue value) continue;
            string? kept = property.Form == SteelDivisionAmmunition.Form.Distance
                ? (inMetres is null ? Number(value) : inMetres(Number(value))) is double metres ? metres.ToString(System.Globalization.CultureInfo.InvariantCulture) : null
                : Written(value);
            if (kept is not null) values[field] = kept;
        }
        if (Find(ammunition, "BaseHitValueModifiers") is NdfValue chances)
        {
            if (NumberFor(chances, "Base") is double always) values[WeaponFileValues.AccuracyBase] = always.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (NumberFor(chances, "Targeted") is double targeted) values[WeaponFileValues.AccuracyTargeted] = targeted.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static string? Written(NdfValue? value) => value switch
    {
        ScalarValue scalar => scalar.Text,
        WrappedValue { Kind: WrapKind.Negative, Inner: ScalarValue inner } => "-" + inner.Text,
        _ => null,
    };

    /// <summary>
    /// Ammunition whose names come again inside its objects, lists and pairs, before and after the ammunition's own,
    /// in WARNO's way and in Steel Division 2's.
    /// </summary>
    private static readonly string[] Ammunitions =
    [
        """
        export Ammo_A is TAmmunitionDescriptor
        (
            HitRollRuleDescriptor = TModernWarfareHitRollRuleDescriptor
            (
                DistanceToTarget = True
                BaseHitValueModifiers = MAP [(EBaseHitValueModifier/Base, 0), (EBaseHitValueModifier/Targeted, 5), (EBaseHitValueModifier/Idling, 40)]
                Name = 'inner'
            )
            Name = 'outer'
            Arme = TDamageTypeRTTI(Family = DamageFamily_ap Index = 32)
            TimeBetweenTwoShots = -0.5
            SimultaneousShotsCount = 2
            TraitsToken = ['STAT', 'HE']
            Caliber = 'C120'
            PiercingWeapon = True
            FireDescriptor = TFireDescriptor(Name = 'fire' TimeBetweenTwoShots = 9)
            MissileDescriptor = $/GFX/Weapon/Missile_A
        )
        """,
        """
        export Ammo_B is TAmmunitionDescriptor
        (
            Arme = EArmeType/AP_26
            PorteeMaximale = ((2100) * Metre)
            PorteeMinimale = ((0) * Metre)
            TempsEntreDeuxSalves = 5.2
            TempsEntreDeuxSalves_Min = 4.0
            NbTirParSalves = 3
            List = [TThing(Name = 'in a list' DistanceToTarget = False), (Caliber, 'pair')]
            DistanceToTarget = True
            Caliber = 'C75'
        )
        """,
        """
        export Ammo_C is TAmmunitionDescriptor
        (
        )
        """,
    ];

    [Fact]
    public void Every_value_is_the_one_found_before()
    {
        int values = 0;
        foreach (string text in Ammunitions)
        {
            var ammunition = (ObjectValue)Parser.Parse(text).Declarations.Single().Value;
            foreach (Func<double?, double?>? inMetres in new Func<double?, double?>?[] { null, distance => distance / 5 })
            {
                Dictionary<string, string> expected = ReadBefore(ammunition, inMetres);
                Dictionary<string, string> read = WeaponFileValues.Read(ammunition, inMetres);
                Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), read.OrderBy(pair => pair.Key, StringComparer.Ordinal));
                values += read.Count;
            }
        }
        Assert.True(values > 20, $"only {values} values read");
    }
}
