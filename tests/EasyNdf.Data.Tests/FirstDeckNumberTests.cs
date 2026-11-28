using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Settings › Armory › First deck number for new units: with none, a new unit gets one after the highest number in
/// use; with one, the first free number from it on, so the game's own new units in an update never take it.
/// </summary>
public class FirstDeckNumberTests
{
    private static readonly int[] Game = [1, 2, 3, 2140];

    [Fact]
    public void Without_a_first_number_counting_goes_on_from_the_highest_in_use() =>
        Assert.Equal(2140, ModUnit.CountFrom(Game, 0, 65535));

    [Fact]
    public void A_far_off_number_a_mod_took_is_left_out() =>
        Assert.Equal(2140, ModUnit.CountFrom([.. Game, 99999], 0, 65535));

    [Fact]
    public void With_a_first_number_counting_starts_there() =>
        Assert.Equal(49999, ModUnit.CountFrom(Game, 50000, 65535));

    [Fact]
    public void With_a_first_number_the_numbers_already_given_from_it_on_are_skipped() =>
        Assert.Equal(50001, ModUnit.CountFrom([.. Game, 50000, 50001], 50000, 65535));

    [Fact]
    public void A_new_unit_made_in_the_Armory_gets_the_first_free_number_from_the_setting()
    {
        var mod = new Mod { Name = "My mod" };
        mod.Units.Add(new ModUnit { Descriptor = "Descriptor_Unit_EZ_A", DeckNumber = 50000 });
        Unit[] game = [new Unit { Descriptor = "Descriptor_Unit_Tank_US", Name = "TANK", DeckNumber = 2140 }];
        Assert.Equal(50001, ModUnit.NextDeckNumber(game, mod, 50000));
        Assert.Equal(50001, ModUnit.NextDeckNumber(game, mod));
        Assert.Equal(2141, ModUnit.NextDeckNumber(game, new Mod { Name = "Other" }));
    }
}
