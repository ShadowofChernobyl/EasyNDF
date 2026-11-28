using System.IO;
using System.Text.Json.Nodes;
using EasyNdf.App.Files;
using EasyNdf.App.Mods;
using EasyNdf.Data;
using EasyNdf.Data.FileList;
using EasyNdf.Data.Game;

namespace EasyNdf.App.Tests;

/// <summary>
/// A mod runs only on files of its own game: each file on the Files tab knows its game, and the mods of another game
/// are left out of what runs on it. The file list kept between sessions keeps each file's game.
/// </summary>
public sealed class FileGameTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EasyNdf.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<Mod> modsMade = [];

    public FileGameTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        foreach (Mod mod in modsMade)
        {
            ModStack.SetOn(mod, false);
            AppData.Current.ModStore.Delete(mod);
        }
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>A mod of a game, switched on, with a rule set for Ammunition.ndf (by its name) and Armory rules for Divisions.ndf.</summary>
    private Mod ModFor(string name, GameTitle game)
    {
        var armoryRule = new JsonObject { ["Name"] = "Points", ["Kind"] = "Edit", ["Armory"] = new JsonObject { ["File"] = "Divisions.ndf" } };
        var mod = new Mod
        {
            Name = name,
            Game = game,
            Presets =
            [
                new ModPreset { Name = name + " (Ammunition.ndf)" },
                new ModPreset { Name = "Divisions", ArmoryPart = ArmoryPart.Divisions, Rules = [armoryRule] },
            ],
        };
        AppData.Current.ModStore.Add(mod);
        modsMade.Add(mod);
        ModStack.SetOn(mod, true);
        return mod;
    }

    private FileToAdd File(string name, GameTitle game)
    {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + name);
        System.IO.File.WriteAllText(path, "A is B()");
        return new FileToAdd(name, "GameData/Gameplay", path) { Game = game };
    }

    private static Task<DuplicateAnswer> KeepBoth(FileEntry listed, FileToAdd picked) =>
        Task.FromResult(new DuplicateAnswer(DuplicateChoice.KeepBoth, false));

    private static List<string> ModsOf(FileEntry entry) => [.. entry.ModsOnFile.Select(mod => mod.Mod)];

    [Fact]
    public async Task Only_the_mods_of_a_files_game_run_on_it()
    {
        Mod warno = ModFor("Game test WARNO mod", GameTitle.Warno);
        Mod steelDivision = ModFor("Game test SD2 mod", GameTitle.SteelDivision2);
        var model = new FileListModel();

        IReadOnlyList<FileEntry> rows = await model.AddFiles(
            [File("Ammunition.ndf", GameTitle.Warno), File("Ammunition.ndf", GameTitle.SteelDivision2), File("Divisions.ndf", GameTitle.SteelDivision2)],
            fromFolder: false, KeepBoth);

        Assert.Contains(warno.Name, ModsOf(rows[0]));
        Assert.DoesNotContain(steelDivision.Name, ModsOf(rows[0]));
        Assert.Contains(steelDivision.Name, ModsOf(rows[1]));
        Assert.DoesNotContain(warno.Name, ModsOf(rows[1]));
        // the Armory's changes too: only the SD2 mod's run on SD2's Divisions.ndf
        Assert.Equal([steelDivision.Name], ModsOf(rows[2]).Where(name => name.StartsWith("Game test", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_mod_whose_game_changes_moves_to_that_games_files()
    {
        Mod mod = ModFor("Game test changing mod", GameTitle.Warno);
        var model = new FileListModel();
        IReadOnlyList<FileEntry> rows = await model.AddFiles([File("Ammunition.ndf", GameTitle.Warno), File("Ammunition.ndf", GameTitle.SteelDivision2)], fromFolder: false, KeepBoth);

        mod.Game = GameTitle.SteelDivision2;
        model.FollowMods();

        Assert.DoesNotContain(mod.Name, ModsOf(rows[0]));
        Assert.Contains(mod.Name, ModsOf(rows[1]));
    }

    [Fact]
    public async Task A_file_added_without_a_game_is_warnos()
    {
        var model = new FileListModel();
        string path = Path.Combine(folder, "Ammunition.ndf");
        System.IO.File.WriteAllText(path, "A is B()");

        IReadOnlyList<FileEntry> rows = await model.AddFiles([FileToAdd.OnDisk(path)], fromFolder: false, KeepBoth);

        Assert.Equal(GameTitle.Warno, rows[0].Game);
    }

    [Fact]
    public void The_kept_file_list_keeps_each_files_game()
    {
        var entry = new FileEntry { Name = "Ammunition.ndf", Folder = "GameData", Source = @"C:\Games\Ammunition.ndf", Game = GameTitle.SteelDivision2 };

        FileSession.KeptRow kept = FileSession.Rows(FileSession.Keep([entry])).Single();

        Assert.Equal(GameTitle.SteelDivision2, kept.Game);
    }

    [Fact]
    public void A_file_kept_before_files_had_a_game_is_warnos()
    {
        var old = new JsonArray(new JsonObject { ["source"] = @"C:\Games\Ammunition.ndf", ["folder"] = "", ["name"] = "Ammunition.ndf", ["on"] = true });
        Assert.Equal(GameTitle.Warno, FileSession.Rows(old).Single().Game);
    }

    [Fact]
    public void Only_the_mods_of_a_game_run_for_it()
    {
        Mod warno = ModFor("Game test running WARNO", GameTitle.Warno);
        Mod steelDivision = ModFor("Game test running SD2", GameTitle.SteelDivision2);

        Assert.Contains(warno, ModStack.RunningFor(GameTitle.Warno));
        Assert.DoesNotContain(steelDivision, ModStack.RunningFor(GameTitle.Warno));
        // an export of WARNO files alone runs no SD2 mod
        IReadOnlyList<Mod> onWarnoFiles = ModStack.RunningOn([new FileEntry { Name = "A.ndf", Game = GameTitle.Warno }]);
        Assert.Contains(warno, onWarnoFiles);
        Assert.DoesNotContain(steelDivision, onWarnoFiles);
    }
}
