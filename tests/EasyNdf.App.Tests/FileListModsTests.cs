using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Tests;

/// <summary>
/// The Files tab's Mods column: which mods run on each file added, with their rule sets and Armory changes. Files added
/// together share one look at the mods; these pin down that every row still gets what a look of its own would give.
/// </summary>
[Collection(AppModsTests.Name)]
public sealed class FileListModsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdf.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Mod> modsMade = [];

    public FileListModsTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        foreach (Mod mod in modsMade) ModStack.SetOn(mod, false);
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>A mod, switched on, with a rule set for Ammunition.ndf (by its name) and Armory rules for Divisions.ndf and UNITS.csv.</summary>
    private Mod ArmoryMod(string name)
    {
        var armoryRule = new JsonObject
        {
            ["Name"] = "Points",
            ["Kind"] = "Edit",
            ["Armory"] = new JsonObject { ["File"] = "Divisions.ndf" },
        };
        var heading = new JsonObject { ["Name"] = "A division", ["Kind"] = "Group", ["Armory"] = new JsonObject { ["File"] = "Heading.ndf" } };
        var mod = new Mod
        {
            Name = name,
            Presets =
            [
                new ModPreset { Name = name + " (Ammunition.ndf)" },
                new ModPreset { Name = "Divisions", ArmoryPart = ArmoryPart.Divisions, Rules = [heading, armoryRule] },
            ],
        };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        ModStack.SetOn(mod, true);
        return mod;
    }

    private FileToAdd File(string name, string subfolder = "GameData/Gameplay")
    {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + name);
        System.IO.File.WriteAllText(path, "A is B()");
        return new FileToAdd(name, subfolder, path);
    }

    private static string ModsOf(FileEntry entry) =>
        string.Join(" | ", entry.ModsOnFile.Select(mod => mod.Mod + ":" + string.Join(",", mod.RuleSets) + ":" + mod.HasArmoryChanges)) + " / " + entry.HasDivisionRules;

    private static Task<DuplicateAnswer> KeepBoth(FileEntry listed, FileToAdd picked) =>
        Task.FromResult(new DuplicateAnswer(DuplicateChoice.KeepBoth, false));

    [Fact]
    public async Task Files_added_together_get_the_mods_that_run_on_each()
    {
        Mod mod = ArmoryMod("Mods column test");
        var model = new FileListModel();

        IReadOnlyList<FileEntry> rows = await model.AddFiles(
            [File("Ammunition.ndf"), File("Divisions.ndf"), File("Heading.ndf"), File("Other.ndf")], fromFolder: false, KeepBoth);

        Assert.Equal(mod.Name + ":" + mod.Name + " (Ammunition.ndf):False / False", ModsOf(rows[0]));
        Assert.Equal(mod.Name + "::True / True", ModsOf(rows[1]));
        // a group heading names no file of its own
        Assert.Equal(" / False", ModsOf(rows[2]));
        Assert.Equal(" / False", ModsOf(rows[3]));
    }

    [Fact]
    public async Task Files_added_together_get_what_marking_the_whole_list_again_gives()
    {
        ArmoryMod("Whole list test");
        var model = new FileListModel();
        await model.AddFiles([File("Ammunition.ndf"), File("Divisions.ndf"), File("Other.ndf"), File("Divisions.ndf", "GameData/Other")], fromFolder: false, KeepBoth);
        List<string> added = [.. model.Entries.Select(ModsOf)];

        // FollowMods looks at the mods once for the whole list, as when a mod changes
        model.FollowMods();

        Assert.Equal(added, model.Entries.Select(ModsOf));
    }

    [Fact]
    public async Task A_mod_switched_on_while_a_duplicate_is_asked_about_runs_on_the_files_added_after()
    {
        Mod mod = ArmoryMod("Switched while asking test");
        ModStack.SetOn(mod, false);
        var model = new FileListModel();
        await model.AddFiles([File("Ammunition.ndf")], fromFolder: false, KeepBoth);

        IReadOnlyList<FileEntry> rows = await model.AddFiles(
            [File("Other.ndf"), File("Ammunition.ndf"), File("Divisions.ndf")], fromFolder: false,
            (listed, picked) =>
            {
                ModStack.SetOn(mod, true);
                return KeepBoth(listed, picked);
            });

        Assert.Equal(" / False", ModsOf(rows[0]));
        Assert.Equal(mod.Name + ":" + mod.Name + " (Ammunition.ndf):False / False", ModsOf(rows[1]));
        Assert.Equal(mod.Name + "::True / True", ModsOf(rows[2]));
    }

    [Fact]
    public void Sorting_by_Armory_gives_each_file_the_letters_of_the_parts_with_rules_for_it()
    {
        Mod mod = ArmoryMod("Armory sort test");
        var battlegroup = new JsonObject { ["Name"] = "Cards", ["Kind"] = "Edit", ["Armory"] = new JsonObject { ["File"] = "Decks.ndf" } };
        mod.Presets.Add(new ModPreset { Name = "Battlegroups", ArmoryPart = ArmoryPart.Battlegroups, Rules = [battlegroup] });
        var name = new JsonObject { ["Name"] = "Name", ["Kind"] = "Edit", ["Armory"] = new JsonObject { ["File"] = "Divisions.ndf", ["Shows"] = "Name" } };
        mod.Presets.Add(new ModPreset { Name = "Units", ArmoryPart = ArmoryPart.Units, Rules = [name] });
        FileEntry Listed(string file, string folder) => new() { Name = file, Folder = folder, Source = Path.Combine(folder, file) };
        FileEntry[] rows =
        [
            Listed("Decks.ndf", "GameData/Generated/Gameplay/Decks"),
            Listed("Other.ndf", "GameData/Generated/Gameplay/Decks"),
            Listed("divisions.NDF", "GameData/Generated/Gameplay/Decks"),
            // the Armory writes only the game's Generated files
            Listed("Divisions.ndf", "GameData/Gameplay/Decks"),
            Listed("UNITS.csv", "GameData/Localisation/Test"),
            Listed("Heading.ndf", "GameData/Generated/Gameplay"),
        ];

        Func<FileEntry, string> keys = FileListModel.ArmorySortKeys();

        Assert.Equal(["B", "", "UD", "", "U", ""], rows.Select(keys));
        // the same as going through every rule for each file
        var everyRule = (Func<FileEntry, string>)typeof(FileRunner).GetMethod("ArmorySortKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<FileEntry, string>));
        Assert.Equal(rows.Select(everyRule), rows.Select(keys));
    }
}
