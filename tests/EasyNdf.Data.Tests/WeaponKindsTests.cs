using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Which sort a weapon is. A HEAT shell's damage family is "ap_missile" (Ammo_Canon_AP_85mm_S53 has
/// DamageFamily_ap_missile), but a shell in Ammunition.ndf is a gun: anti-armour, not a missile.
/// </summary>
public sealed class WeaponKindsTests
{
    [Fact]
    public void A_HEAT_shell_is_anti_armour_not_a_missile() =>
        Assert.Equal(WeaponKind.AntiArmor, WeaponKinds.Of("Canon AP 85mm S53", "ap_missile", "Ammunition.ndf", null));

    [Fact]
    public void Missiles_stay_missiles()
    {
        // by their file, their missile, or their family where the file is not known
        Assert.Equal(WeaponKind.Missile, WeaponKinds.Of("ATGM 9M113 Konkurs", "ap_missile", "AmmunitionMissiles.ndf", "Missile_Konkurs"));
        Assert.Equal(WeaponKind.Missile, WeaponKinds.Of("Gun launched 9M117 Bastion", "ap_missile", "Ammunition.ndf", "Missile_9M117"));
        Assert.Equal(WeaponKind.Missile, WeaponKinds.Of("Some missile", "missile_he", null, null));
    }

    [Fact]
    public void Rockets_and_smoke_keep_their_sort()
    {
        Assert.Equal(WeaponKind.Rocket, WeaponKinds.Of("RocketInf M67 RCL 90mm", "ap_missile", "Ammunition.ndf", null));
        Assert.Equal(WeaponKind.Smoke, WeaponKinds.Of("9M53 SMOKE", "ap_missile", "Ammunition.ndf", null));
        Assert.Equal(WeaponKind.AntiArmor, WeaponKinds.Of("Canon AP 120mm", "ap", "Ammunition.ndf", null));
    }
}
