using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class UnitFilterTests
{
    private static Unit Make(string name, string side, string nation, string factory, string role = "armor", string[]? divisions = null, double? cost = null) => new()
    {
        Descriptor = "Descriptor_Unit_" + name.Replace(' ', '_') + "_" + nation, Name = name, Side = side, Nation = nation,
        Factory = factory, Role = role, Divisions = divisions ?? ["1st"], Cost = cost,
    };

    private static readonly Unit[] Units =
    [
        Make("Abrams", "NATO", "US", "Tanks", cost: 275),
        Make("Bradley", "NATO", "US", "Infantry", "ifv", cost: 90),
        Make("Leopard", "NATO", "RFA", "Tanks", divisions: []),
        Make("T-80", "PACT", "SOV", "Tanks", cost: 310),
        Make("Romanian gun", "Allied", "ROU", "Art"),
        Make("Romanian tank", "Axis", "ROU", "Tanks"),
    ];

    [Fact]
    public void By_default_every_unit_is_shown_and_units_in_no_division_can_be_hidden()
    {
        var filter = new UnitFilter();

        Assert.Equal(6, filter.Apply(Units).Count);
        filter.OnlyInDivision = true;
        Assert.DoesNotContain(filter.Apply(Units), unit => unit.Name == "Leopard");
    }

    [Fact]
    public void Only_a_weapons_units_are_shown_and_counted_and_the_other_filters_narrow_them_further()
    {
        var filter = new UnitFilter { Carrying = new HashSet<string> { Units[0].Descriptor, Units[3].Descriptor } };

        Assert.Equal(["Abrams", "T-80"], filter.Apply(Units).Select(u => u.Name));
        Assert.Equal(2, filter.FactoryCounts(Units)["Tanks"]);
        Assert.False(filter.FactoryCounts(Units).ContainsKey("Infantry"));
        Assert.Equal(1, filter.NationCounts(Units, "NATO")["US"]);

        filter.Side = "PACT";
        Assert.Equal(["T-80"], filter.Apply(Units).Select(u => u.Name));
    }

    [Fact]
    public void Each_filter_narrows_the_list()
    {
        var filter = new UnitFilter { Side = "NATO", OnlyInDivision = true };
        Assert.Equal(["Abrams", "Bradley"], filter.Apply(Units).Select(u => u.Name));

        filter.Factory = "Tanks";
        Assert.Equal(["Abrams"], filter.Apply(Units).Select(u => u.Name));

        filter = new UnitFilter { Text = "bRAD" };
        Assert.Equal(["Bradley"], filter.Apply(Units).Select(u => u.Name));

        filter = new UnitFilter { Text = "_SOV" };
        Assert.Equal(["T-80"], filter.Apply(Units).Select(u => u.Name));

        filter = new UnitFilter { Role = "ifv" };
        Assert.Equal(["Bradley"], filter.Apply(Units).Select(u => u.Name));

        // a division chosen: only its units, whether or not they are in another; "Only units in a division" does not count
        filter = new UnitFilter { InDivision = new HashSet<string>([Units[0].Descriptor]) };
        Assert.Equal([Units[0].Name], filter.Apply(Units).Select(u => u.Name));
    }

    [Fact]
    public void Ticking_nations_shows_only_theirs()
    {
        var filter = new UnitFilter();
        filter.Nations.Add("SOV");
        filter.Nations.Add("ROU");

        Assert.Equal(["T-80", "Romanian gun", "Romanian tank"], filter.Apply(Units).Select(u => u.Name));
    }

    [Fact]
    public void Factory_counts_ignore_the_factory_and_nation_counts_the_nations()
    {
        var filter = new UnitFilter { Factory = "Tanks", OnlyInDivision = true };
        filter.Nations.Add("US");

        // the tabs count as if no tab were picked, but the nations still count
        Assert.Equal(new Dictionary<string, int> { ["Tanks"] = 1, ["Infantry"] = 1 }, filter.FactoryCounts(Units));
        // the nation list counts as if no nation were ticked and no tab picked, side by side
        Assert.Equal(new Dictionary<string, int> { ["US"] = 2 }, filter.NationCounts(Units, "NATO"));
        Assert.Equal(new Dictionary<string, int> { ["ROU"] = 1 }, filter.NationCounts(Units, "Axis"));
    }

    [Fact]
    public void The_best_of_a_stat_is_the_highest_or_for_cost_the_lowest()
    {
        UnitStat cost = UnitStat.All.Single(stat => stat.Key == "Cost");
        UnitStat strength = UnitStat.All.Single(stat => stat.Key == "Strength");

        Assert.Equal(90, cost.Best(Units));
        Assert.Null(strength.Best(Units));
        Assert.Null(cost.Best(Units.Take(1)));
        Assert.Equal(1, cost.Feel(-10));
        Assert.Equal(-1, strength.Feel(-1));
        Assert.Equal(0, strength.Feel(0));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 1)]
    [InlineData(200.0, 7)]
    [InlineData(400.0, 14)]
    [InlineData(9000.0, 14)]
    public void A_bar_fills_whole_blocks(double value, int blocks) => Assert.Equal(blocks, UnitStat.BlocksFor(value, 400, 14));

    [Fact]
    public void Weapons_are_grouped_by_sort()
    {
        UnitWeapon Weapon(string name, string family, string? file = "Ammunition.ndf", string? missile = null) =>
            new(name, 1, family, 1, 1000, 1, 1, 1, "Ammo_" + name.Replace(' ', '_'), file, missile);
        UnitWeapon[] weapons =
        [
            Weapon("MMG M240", "fmballe"),
            Weapon("Canon AP 120mm", "ap"),
            Weapon("SMOKE Vehicle Grenadex8", "he"),
            Weapon("SAM Stinger", "missile_he", "AmmunitionMissiles.ndf", "Descriptor_Missile_Stinger"),
            Weapon("RocketInf M67 RCL 90mm", "ap_missile"),
            Weapon("Canon HE 120mm", "he"),
            Weapon("Balle Mauser", "Balle"),
        ];

        var groups = WeaponKinds.Grouped(weapons);

        Assert.Equal([WeaponKind.AntiArmor, WeaponKind.Missile, WeaponKind.Rocket, WeaponKind.HighExplosive, WeaponKind.SmallArms, WeaponKind.Smoke], groups.Select(g => g.Kind));
        Assert.Equal([0, 6], groups.Single(g => g.Kind == WeaponKind.SmallArms).Weapons.Select(w => w.Index));
    }

    [Fact]
    public void Only_modded_units_leaves_out_the_games_own()
    {
        var filter = new UnitFilter { OnlyModded = true, VanillaUnits = new HashSet<string>(Units.Take(4).Select(unit => unit.Descriptor)) };

        Assert.Equal(["Romanian gun", "Romanian tank"], filter.Apply(Units).Select(unit => unit.Name));
        filter.OnlyModded = false;
        Assert.Equal(6, filter.Apply(Units).Count);
    }
}
