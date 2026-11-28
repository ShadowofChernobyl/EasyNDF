using EasyNdf.Engine.Rules;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Mods that run together, one after the other: a value two of them change is a conflict, which the later mod wins,
/// unless they set it the same; two of them making one descriptor name is a name clash, which the first one wins.
/// </summary>
public class ModConflictFinderTests
{
    private static (string, int, EditLogEntry) Change(string rule, string descriptor, string target, string old, string now) =>
        (rule, 0, new EditLogEntry(descriptor, target, old, now));

    [Fact]
    public void A_value_two_mods_change_is_won_by_the_later_one_and_values_only_one_changes_are_not_conflicts()
    {
        List<ConflictedValue> found = ModConflictFinder.Find("Ammunition.ndf",
        [
            ("WTO", [Change("Range", "Ammo_A", "MaximumRangeGRU", "2100", "2275"), Change("Damage", "Ammo_B", "PhysicalDamages", "1.0", "1.2")]),
            ("My mod", [Change("First", "Ammo_A", "MaximumRangeGRU", "2275", "2000"), Change("Again", "Ammo_A", "MaximumRangeGRU", "2000", "2400"),
                Change("Supply", "Ammo_C", "SupplyCost", "12", "10")]),
        ]);

        ConflictedValue value = Assert.Single(found);
        Assert.Equal(("Ammo_A", "MaximumRangeGRU"), (value.Descriptor, value.Property));
        Assert.Equal(["WTO", "My mod"], value.Changes.Select(change => change.Mod));
        // a mod that changes the value twice counts once, with its last change
        Assert.Equal(("Again", "2400"), (value.Kept.Rule, value.Kept.New));
        Assert.Equal(new ModConflict("Ammunition.ndf", "Ammo_A", "MaximumRangeGRU", "My mod", "WTO"), Assert.Single(value.Pairs()));
    }

    [Fact]
    public void A_value_three_mods_change_gives_each_earlier_mod_against_the_last()
    {
        ConflictedValue value = Assert.Single(ModConflictFinder.Find("Units.ndf",
        [
            ("First", [Change("A", "Unit", "MaxSpeedInKmph", "230", "240")]),
            ("Second", [Change("B", "Unit", "MaxSpeedInKmph", "240", "250")]),
            ("Third", [Change("C", "Unit", "MaxSpeedInKmph", "250", "260")]),
        ]));

        Assert.Equal(["First", "Second"], value.Pairs().Select(conflict => conflict.Overridden));
        Assert.All(value.Pairs(), conflict => Assert.Equal("Third", conflict.Winner));
    }

    [Fact]
    public void Mods_that_set_a_value_the_same_lose_nothing_and_notes_are_not_changes()
    {
        List<ConflictedValue> found = ModConflictFinder.Find("Ammunition.ndf",
        [
            ("WTO", [Change("Range", "Ammo_A", "MaximumRangeGRU", "2100", "2275"), ("Check", 1, new EditLogEntry("Range rule", "Check", "", "Failed.", IsNote: true))]),
            ("YSM", [Change("Range", "Ammo_A", "MaximumRangeGRU", "2275", "2275"), ("Check", 1, new EditLogEntry("Range rule", "Check", "", "Failed.", IsNote: true))]),
        ]);

        Assert.Empty(found);
    }

    [Fact]
    public void Two_mods_making_one_name_is_a_clash_the_first_one_wins_and_a_name_the_game_has_is_not()
    {
        List<ConflictedValue> found = ModConflictFinder.Find("UniteDescriptor.ndf",
        [
            ("YSM", [Change("Add M1A2", "Descriptor_Unit_M1A2", "Clone", "", "Cloned from \"Descriptor_Unit_M1A1HA\"."),
                Change("Own T-80", "Descriptor_Unit_T80", "Clone", "", "Not cloned. \"Descriptor_Unit_T80\" is already in the file.")]),
            ("My mod", [Change("My Abrams", "Descriptor_Unit_M1A2", "Clone", "", "Not cloned. \"Descriptor_Unit_M1A2\" is already in the file."),
                Change("My T-80", "Descriptor_Unit_T80", "Clone", "", "Not cloned. \"Descriptor_Unit_T80\" is already in the file.")]),
        ]);

        ConflictedValue clash = Assert.Single(found);
        Assert.True(clash.IsNameClash);
        Assert.Equal("Descriptor_Unit_M1A2", clash.Descriptor);
        Assert.Equal("YSM", clash.Kept.Mod);
        Assert.Equal(new ModConflict("UniteDescriptor.ndf", "Descriptor_Unit_M1A2", "Clone", "YSM", "My mod"), Assert.Single(clash.Pairs()));
    }

    [Fact]
    public void A_values_lines_are_found_by_its_descriptor_then_each_part_of_its_target()
    {
        string text = string.Join("\r\n",
            "export Descriptor_Unit_A is TEntityDescriptor",
            "(",
            "    ModulesDescriptors = [",
            "        TModuleDescriptor",
            "        (",
            "            MaxSpeedInKmph = 100",
            "        ),",
            "        TOtherModuleDescriptor",
            "        (",
            "            MaxSpeedInKmph = 230",
            "        ),",
            "    ]",
            ")",
            "export Descriptor_Unit_B is TEntityDescriptor",
            "(",
            "    MaxSpeedInKmph = 50",
            ")");
        var texts = new ValueTexts(text);

        ValueText nested = texts.Find("Descriptor_Unit_A", "ModulesDescriptors > TOtherModuleDescriptor > MaxSpeedInKmph")!;
        Assert.Equal("            MaxSpeedInKmph = 230", nested.Lines[nested.At]);
        // the lines stay inside the descriptor
        Assert.DoesNotContain(nested.Lines, line => line.Contains("Descriptor_Unit_B"));

        ValueText other = texts.Find("Descriptor_Unit_B", "MaxSpeedInKmph")!;
        Assert.Equal("    MaxSpeedInKmph = 50", other.Lines[other.At]);

        ValueText made = texts.Find("Descriptor_Unit_B", "Clone")!;
        Assert.Equal("export Descriptor_Unit_B is TEntityDescriptor", made.Lines[made.At]);

        Assert.Null(texts.Find("Descriptor_Unit_C", "MaxSpeedInKmph"));
    }
}
