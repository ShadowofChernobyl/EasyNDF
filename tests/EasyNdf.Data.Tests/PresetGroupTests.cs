namespace EasyNdf.Data.Tests;

/// <summary>
/// A mod's rule sets can be put in groups, as the Rules tab lists them: the groups, folded or not, and which group each
/// rule set is in, kept in the mod's file, its export and a backup.
/// </summary>
public class PresetGroupTests
{
    private static Mod Grouped() => new()
    {
        Name = "YSM",
        Presets =
        [
            new ModPreset { Name = "YSM Cheat Units (Ammunition.ndf)", Group = "Cheat Units" },
            new ModPreset { Name = "YSM Intro Video (Intro.ndf)" },
        ],
        PresetGroups = [new PresetGroup { Name = "Cheat Units" }, new PresetGroup { Name = "Zombies", IsFolded = true }],
    };

    [Fact]
    public void A_mod_keeps_its_groups_and_each_rule_sets_group_in_its_file_its_export_and_a_backup()
    {
        foreach (Mod read in new[]
        {
            ModFiles.Read(ModFiles.StoredText(Grouped()), "x.mod.json").Single(),
            ModFiles.Read(ModFiles.ExportText(Grouped(), isOn: true), "x.mod.json").Single(),
            ModFiles.Read(ModFiles.BackupText([Grouped()]), "backup.json").Single(),
        })
        {
            Assert.Equal(["Cheat Units", "Zombies"], read.PresetGroups.Select(group => group.Name));
            Assert.True(read.PresetGroups[1].IsFolded);
            Assert.Equal("Cheat Units", read.Presets[0].Group);
            Assert.Null(read.Presets[1].Group);
        }
    }

    [Fact]
    public void A_group_a_rule_set_names_but_the_list_leaves_out_is_added()
    {
        Mod mod = ModFiles.Read("""{ "ModName": "M", "Presets": [ { "PresetName": "A", "Rules": [], "Group": "Lost" } ] }""", "M.mod.json").Single();
        Assert.Equal(["Lost"], mod.PresetGroups.Select(group => group.Name));
    }

    [Fact]
    public void A_copy_keeps_the_groups_and_changing_one_leaves_the_other()
    {
        Mod mod = Grouped();
        Mod copy = mod.DeepCopy();
        copy.PresetGroups[0].Name = "Renamed";
        copy.Presets[0].Group = "Renamed";
        Assert.Equal("Cheat Units", mod.PresetGroups[0].Name);
        Assert.Equal("Cheat Units", mod.Presets[0].Group);
    }
}
