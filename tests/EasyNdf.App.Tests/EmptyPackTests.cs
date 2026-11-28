using EasyNdf.App.Divisions;

namespace EasyNdf.App.Tests;

/// <summary>
/// The least share at which a WARNO card still holds a unit, which the veterancy pop-up and the Armory's table raise a
/// share to rather than let the card hold none (the game refuses to generate such a mod from v210942 on).
/// </summary>
public sealed class EmptyPackTests
{
    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.25)]
    [InlineData(3, 0.17)]
    [InlineData(4, 0.13)]
    [InlineData(10, 0.05)]
    [InlineData(100, 0.01)]
    public void Least_share_is_the_smallest_two_decimal_share_that_holds_a_unit(int unitsPerCard, double least)
    {
        Assert.Equal(least, DivisionNames.LeastShare(unitsPerCard));
        Assert.False(DivisionNames.IsEmptyPack(unitsPerCard, least));
        if (least > 0.01) Assert.True(DivisionNames.IsEmptyPack(unitsPerCard, Math.Round(least - 0.01, 2)));
    }

    [Fact]
    public void A_share_that_leaves_no_unit_is_raised_and_others_are_kept()
    {
        Assert.Equal(0.13, DivisionNames.HoldingAUnit(4, 0.1));
        Assert.Equal(0, DivisionNames.HoldingAUnit(4, 0));
        Assert.Equal(0.4, DivisionNames.HoldingAUnit(4, 0.4));
        // the game's own 2 units at 0.4: 0.8 rounds to one unit
        Assert.Equal(0.4, DivisionNames.HoldingAUnit(2, 0.4));
        // the game's old F-18 SEAD, 1 unit at 0.4, which v210942 set to 0
        Assert.Equal(0.5, DivisionNames.HoldingAUnit(1, 0.4));
    }
}
