using EasyNdf.Data.Units;
using EasyNdf.Data.Weapons;

namespace EasyNdf.Data.Tests;

/// <summary>The Weapons sub-tab's filters: a weapon's side and nations are those of the units that carry it.</summary>
public class WeaponFilterTests
{
    private static readonly Dictionary<string, Unit> Units = new()
    {
        ["Descriptor_Unit_Tank_US"] = new Unit { Descriptor = "Descriptor_Unit_Tank_US", Name = "TANK", Side = "NATO", Nation = "US" },
        ["Descriptor_Unit_Tank_SOV"] = new Unit { Descriptor = "Descriptor_Unit_Tank_SOV", Name = "T-80", Side = "PACT", Nation = "SOV" },
    };

    private static readonly Weapon[] Weapons =
    [
        new() { Descriptor = "Ammo_Canon_AP_120mm", File = "Ammunition.ndf", Name = "Canon AP 120mm", Kind = WeaponKind.AntiArmor, Category = WeaponCategory.Gun, DamageFamily = "ap", Users = ["Descriptor_Unit_Tank_US"] },
        new() { Descriptor = "Ammo_MMG_PKT", File = "Ammunition.ndf", Name = "MMG PKT", Kind = WeaponKind.SmallArms, Category = WeaponCategory.MachineGun, DamageFamily = "fmballe", Users = ["Descriptor_Unit_Tank_SOV", "Descriptor_Unit_Tank_US"] },
        new() { Descriptor = "Ammo_Unused", File = "Ammunition.ndf", Name = "Unused", Kind = WeaponKind.SmallArms, Category = WeaponCategory.MachineGun, DamageFamily = "fmballe" },
    ];

    [Fact]
    public void A_weapon_is_on_the_side_and_of_the_nations_of_the_units_that_carry_it()
    {
        var filter = new WeaponFilter { Side = "PACT" };
        Assert.Equal(["Ammo_MMG_PKT"], filter.Apply(Weapons, Units).Select(weapon => weapon.Descriptor));

        filter.Side = "";
        filter.Nations.Add("US");
        Assert.Equal(["Ammo_Canon_AP_120mm", "Ammo_MMG_PKT"], filter.Apply(Weapons, Units).Select(weapon => weapon.Descriptor));
        Assert.Equal(2, filter.NationCounts(Weapons, Units)["US"]);
        Assert.Equal(1, filter.NationCounts(Weapons, Units)["SOV"]);
    }

    [Fact]
    public void The_type_tabs_count_with_every_other_filter_and_the_text_finds_names_and_descriptors()
    {
        var filter = new WeaponFilter { Category = WeaponCategory.MachineGun, OnlyInUse = true };
        Assert.Equal(["Ammo_MMG_PKT"], filter.Apply(Weapons, Units).Select(weapon => weapon.Descriptor));
        Assert.Equal((1, 1), (filter.CategoryCounts(Weapons, Units)[WeaponCategory.Gun], filter.CategoryCounts(Weapons, Units)[WeaponCategory.MachineGun]));

        var byText = new WeaponFilter { Text = "ammo_canon" };
        Assert.Equal(["Ammo_Canon_AP_120mm"], byText.Apply(Weapons, Units).Select(weapon => weapon.Descriptor));
    }

    [Fact]
    public void Only_modded_weapons_can_be_shown_those_the_vanilla_game_does_not_have()
    {
        var filter = new WeaponFilter { OnlyModded = true, VanillaWeapons = new HashSet<string> { "Ammo_Canon_AP_120mm", "Ammo_Unused" } };
        Assert.Equal(["Ammo_MMG_PKT"], filter.Apply(Weapons, Units).Select(weapon => weapon.Descriptor));
        Assert.Equal(1, filter.CategoryCounts(Weapons, Units).Values.Sum());
    }

    [Fact]
    public void A_weapons_category_is_the_word_after_Ammo_in_its_descriptor_else_its_sort()
    {
        Assert.Equal(WeaponCategory.Gun, WeaponCategories.Of("Ammo_Canon_AP_120mm", WeaponKind.AntiArmor));
        Assert.Equal(WeaponCategory.MachineGun, WeaponCategories.Of("Ammo_hmg_M2HB", WeaponKind.SmallArms));
        Assert.Equal(WeaponCategory.Air, WeaponCategories.Of("Ammo_RocketAir_Hydra_70mm", WeaponKind.Rocket));
        Assert.Equal(WeaponCategory.Artillery, WeaponCategories.Of("Ammo_Howz_Canon_M109", WeaponKind.HighExplosive));
        Assert.Equal(WeaponCategory.AntiTankMissile, WeaponCategories.Of("Ammunition_MILAN", WeaponKind.Missile));
        Assert.True(WeaponCategories.IsHeavyMachineGun("Ammo_HMG_M2HB"));
    }
}
