namespace EasyNdf.Data.Tests;

/// <summary>What is typed in the Armory's Find boxes finds names and descriptors alike.</summary>
public class TypedTests
{
    [Theory]
    [InlineData("Descriptor_Unit_M1A1_Abrams_US", "M1A1 Abrams", true)]
    [InlineData("Descriptor_Unit_M1A1_Abrams_US", "m1a1_abrams", true)]
    [InlineData("M1A1 Abrams", "", true)]
    [InlineData("M1A1 Abrams", "T-80", false)]
    public void A_space_and_an_underscore_match_each_other_in_any_case(string text, string typed, bool found) =>
        Assert.Equal(found, Typed.IsIn(text, typed));
}
