using Xunit;

namespace EasyNdf.Data.Tests;

/// <summary>The order a mod's rule sets are listed and run in: no group first, as kept; then group by group, in the order the groups are kept; the Armory's presets last.</summary>
public class PresetsInOrderTests
{
    private static ModPreset Set(string name, string? group = null) => new() { Name = name, Group = group };

    [Fact]
    public void RuleSetsOfNoGroupKeepTheOrderTheyAreKeptIn()
    {
        var mod = new Mod { Presets = [Set("Zulu"), Set("Alpha"), Set("Mike")] };
        Assert.Equal(["Zulu", "Alpha", "Mike"], mod.PresetsInOrder().Select(set => set.Name));
    }

    [Fact]
    public void GroupsComeAfterInTheOrderGivenThemEachInItsOwnOrder()
    {
        var mod = new Mod
        {
            Presets = [Set("in B 2", "B"), Set("loose 1"), Set("in A", "A"), Set("in B 1", "B"), Set("loose 2")],
            PresetGroups = [new PresetGroup { Name = "B" }, new PresetGroup { Name = "A" }],
        };
        Assert.Equal(["loose 1", "loose 2", "in B 2", "in B 1", "in A"], mod.PresetsInOrder().Select(set => set.Name));
    }

    [Fact]
    public void ARuleSetWhoseGroupIsGoneCountsAsInNoGroup()
    {
        var mod = new Mod { Presets = [Set("one", "Gone"), Set("two")] };
        Assert.Equal(["one", "two"], mod.PresetsInOrder().Select(set => set.Name));
    }
}
