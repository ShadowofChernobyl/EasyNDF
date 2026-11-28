using EasyNdf.Data.FileList;

namespace EasyNdf.Data.Tests;

/// <summary>Which files a mod's rule sets apply to: by their names, as presets always were, or the files listed in Applies To.</summary>
public class RuleSetFilesTests
{
    private static ModPreset RuleSet(string name, params string[] appliesTo) =>
        new() { Name = name, AppliesTo = appliesTo.Length == 0 ? null : [.. appliesTo] };

    private static List<string> Names(IEnumerable<ModPreset> ruleSets) => [.. ruleSets.Select(ruleSet => ruleSet.Name)];

    [Fact]
    public void A_rule_set_applies_to_the_file_its_name_holds()
    {
        ModPreset[] ruleSets = [RuleSet("WTO (Ammunition.ndf)"), RuleSet("WTO (UniteDescriptor.ndf)")];
        Assert.Equal(["WTO (Ammunition.ndf)"], Names(RuleSetFiles.For("Ammunition.ndf", "GameData/Generated/Gameplay/Gfx", ruleSets, byName: true)));
        // with "Apply rule sets by file name" off, only files listed in Applies To count
        Assert.Empty(RuleSetFiles.For("Ammunition.ndf", "GameData/Generated/Gameplay/Gfx", ruleSets, byName: false));
    }

    [Fact]
    public void The_name_naming_more_of_the_files_folders_wins()
    {
        ModPreset[] ruleSets = [RuleSet("WTO (GDConstants.ndf)"), RuleSet("WTO (Strategic/GDConstants.ndf)")];
        Assert.Equal(["WTO (Strategic/GDConstants.ndf)"], Names(RuleSetFiles.For("GDConstants.ndf", "Gameplay/Constantes/Strategic", ruleSets, byName: true)));
        Assert.Equal(["WTO (GDConstants.ndf)"], Names(RuleSetFiles.For("GDConstants.ndf", "Gameplay/Constantes", ruleSets, byName: true)));
    }

    [Fact]
    public void Two_rule_sets_of_one_mod_for_the_same_file_both_apply_in_list_order()
    {
        ModPreset[] ruleSets = [RuleSet("Ranges", "Ammunition.ndf", "AmmunitionMissiles.ndf"), RuleSet("Ammo_Canon fixes"), RuleSet("WTO (Ammunition.ndf)")];
        Assert.Equal(["Ranges", "WTO (Ammunition.ndf)"], Names(RuleSetFiles.For("Ammunition.ndf", "Gfx", ruleSets, byName: true)));
        Assert.Equal(["Ranges"], Names(RuleSetFiles.For("AmmunitionMissiles.ndf", "Gfx", ruleSets, byName: true)));
    }

    [Fact]
    public void A_listed_file_with_folders_applies_only_to_the_file_in_them()
    {
        ModPreset ruleSet = RuleSet("Strategic only", "Strategic/GDConstants.ndf");
        Assert.True(RuleSetFiles.Lists(ruleSet, "GDConstants.ndf", "Gameplay/Constantes/Strategic"));
        Assert.False(RuleSetFiles.Lists(ruleSet, "GDConstants.ndf", "Gameplay/Constantes"));
        // a rule set with files of its own no longer applies by its name
        Assert.Empty(RuleSetFiles.For("Ammunition.ndf", "", [RuleSet("WTO (Ammunition.ndf)", "Terrains.ndf")], byName: true));
    }

    [Fact]
    public void Many_rule_sets_and_files_give_what_matching_each_one_on_its_own_gives()
    {
        var random = new Random(7);
        string[] names = ["GDConstants.ndf", "Ammunition.ndf", "Decks.ndf", "UNITS.csv", "gdconstants.ndf"];
        string[] folders = ["", "Gameplay", "Gameplay/Constantes", "Gameplay/Constantes/Strategic", "GameData/Generated/Gameplay/Decks", "Strategic"];
        string Path(string name) => folders[random.Next(folders.Length)] is { Length: > 0 } folder && random.Next(2) == 0
            ? (random.Next(2) == 0 ? folder.Replace('/', '\\') : folder) + (random.Next(3) == 0 ? "\\" : "/") + name : name;
        for (int round = 0; round < 300; round++)
        {
            var ruleSets = new List<ModPreset>();
            for (int each = random.Next(8); each > 0; each--)
            {
                string named = "Set " + each + " (" + Path(names[random.Next(names.Length)]) + ")";
                ModPreset ruleSet = random.Next(3) == 0 ? RuleSet(named, [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => Path(names[random.Next(names.Length)]))]) : RuleSet(named);
                if (random.Next(6) == 0) ruleSet.ArmoryPart = ArmoryPart.Divisions;
                ruleSets.Add(ruleSet);
            }
            string fileName = names[random.Next(names.Length)], folder = folders[random.Next(folders.Length)];
            foreach (bool byName in new[] { true, false })
                Assert.Equal(Names(EachOnItsOwn(fileName, folder, ruleSets, byName)), Names(RuleSetFiles.For(fileName, folder, ruleSets, byName)));
        }
    }

    /// <summary>
    /// The rule sets that apply to a file, worked out the plain way: every rule set's name fit, the best ones by name,
    /// then those and the ones listing the file, in list order.
    /// </summary>
    private static List<ModPreset> EachOnItsOwn(string fileName, string folder, List<ModPreset> ruleSets, bool byName)
    {
        string[] folders = folder.ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        List<ModPreset> own = [.. ruleSets.Where(ruleSet => !ruleSet.IsArmoryPreset)];
        var fits = own.Where(ruleSet => byName && ruleSet.AppliesTo is null).Select(ruleSet => (ruleSet, fit: PresetMatching.Fit(ruleSet.Name, fileName, folders))).Where(each => each.fit >= 0).ToList();
        HashSet<ModPreset> best = fits.Count == 0 ? [] : [.. fits.Where(each => each.fit == fits.Max(other => other.fit)).Select(each => each.ruleSet)];
        bool Listed(ModPreset ruleSet) => ruleSet.AppliesTo is List<string> listed && listed.Any(file =>
        {
            string written = file.Replace('\\', '/');
            return written[(written.LastIndexOf('/') + 1)..] == fileName && PresetMatching.Fit(written, fileName, folders) >= 0;
        });
        return [.. own.Where(ruleSet => best.Contains(ruleSet) || Listed(ruleSet))];
    }

    [Fact]
    public void A_listed_file_is_matched_by_its_whole_name_whichever_way_its_folders_are_written()
    {
        ModPreset backslashes = RuleSet("Strategic only", @"Constantes\Strategic\GDConstants.ndf");
        Assert.True(RuleSetFiles.Lists(backslashes, "GDConstants.ndf", "Gameplay/Constantes/Strategic"));
        Assert.False(RuleSetFiles.Lists(backslashes, "GDConstants.ndf", "Gameplay/Strategic"));
        ModPreset mixed = RuleSet("Mixed", @"Gameplay/Constantes\GDConstants.ndf");
        Assert.True(RuleSetFiles.Lists(mixed, "GDConstants.ndf", "Gameplay/Constantes"));
        // a name that only ends like the file's, or differs in capitals, is another file
        ModPreset others = RuleSet("Others", "MyGDConstants.ndf", "gdconstants.ndf", @"Strategic\XGDConstants.ndf");
        Assert.False(RuleSetFiles.Lists(others, "GDConstants.ndf", "Gameplay/Constantes/Strategic"));
        ModPreset[] ruleSets = [others, mixed, backslashes, RuleSet("WTO (GDConstants.ndf)")];
        Assert.Equal(["Strategic only", "WTO (GDConstants.ndf)"], Names(RuleSetFiles.For("GDConstants.ndf", "Gameplay/Constantes/Strategic", ruleSets, byName: true)));
        Assert.Equal(["Mixed", "WTO (GDConstants.ndf)"], Names(RuleSetFiles.For("GDConstants.ndf", "Gameplay/Constantes", ruleSets, byName: true)));
    }

    [Fact]
    public void The_files_a_rule_set_names_come_from_its_list_or_its_name()
    {
        Assert.Equal(["Ammunition.ndf"], RuleSetFiles.Named(RuleSet("WTO (Ammunition.ndf)")));
        Assert.Equal(["Strategic/GDConstants.ndf"], RuleSetFiles.Named(RuleSet("WTO v1.2 (Strategic/GDConstants.ndf)")));
        Assert.Empty(RuleSetFiles.Named(RuleSet("Ranges (all ammunition)")));
        Assert.Equal(["A.ndf", "B.ndf"], RuleSetFiles.Named(RuleSet("Mine", "A.ndf", "B.ndf")));
    }

    [Fact]
    public void A_mod_file_keeps_a_rule_sets_files()
    {
        var mod = new Mod { Name = "Test", Presets = [RuleSet("Ranges", "Ammunition.ndf", "Strategic/GDConstants.ndf"), RuleSet("WTO (Terrains.ndf)")] };
        Mod read = ModFiles.Read(ModFiles.ExportText(mod), "Test.mod.json")[0];
        Assert.Equal(["Ammunition.ndf", "Strategic/GDConstants.ndf"], read.Presets[0].AppliesTo);
        Assert.Null(read.Presets[1].AppliesTo);
        // a rule set going by its name writes nothing more, so the file stays as other apps write it
        Assert.DoesNotContain("AppliesTo", ModFiles.ExportText(new Mod { Name = "Test", Presets = [RuleSet("WTO (Terrains.ndf)")] }));
    }
}
