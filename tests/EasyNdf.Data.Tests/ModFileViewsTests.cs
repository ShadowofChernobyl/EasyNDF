using System.Text;
using System.Text.Json.Nodes;
using EasyNdf.Engine.Mods;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Each mod's files in a folder beside it: moved there from where they wait, found there by the name mods give them,
/// back to wait when no mod's folder has them, and cleaned up only when asked.
/// </summary>
public class ModFileViewsTests
{
    private sealed class Data : IDisposable
    {
        private readonly TemporaryFolder folder = new();
        public string Mods => Path.Combine(folder.Path, "Mods");
        public string Pictures => Path.Combine(folder.Path, "Pictures");
        public ModFileStore Store { get; }
        public ModFileViews Views { get; }

        public Data()
        {
            Directory.CreateDirectory(Mods);
            Store = new ModFileStore(Path.Combine(folder.Path, "Mod Files"));
            Views = new ModFileViews(Mods, Store, Pictures);
        }

        public void Dispose() => folder.Dispose();

        /// <summary>Saves a mod's file in its folder, as the app does, and lays the folder out.</summary>
        public void Sync(string stem, string text)
        {
            Directory.CreateDirectory(Path.Combine(Mods, stem));
            File.WriteAllText(Path.Combine(Mods, stem, stem + ".mod.json"), text);
            Views.Sync(stem, text);
        }
    }

    private static string ModText(params (string Destination, string Stored)[] files)
    {
        var rules = new JsonArray();
        foreach ((string destination, string stored) in files)
        {
            JsonObject rule = RuleLoading.NewRule("File");
            rule["Params"]!["Destination"] = destination;
            rule["Params"]!["Stored"] = stored;
            rule["Params"]!["ChosenName"] = "Logo.png";
            rules.Add(rule);
        }
        return new JsonObject { ["Presets"] = new JsonArray { new JsonObject { ["Rules"] = rules } } }.ToJsonString();
    }

    private const string Flag = @"GameData\Assets\2D\Flags\SOV.png";

    [Fact]
    public void A_kept_file_moves_into_its_mods_folder_and_is_found_there()
    {
        using var data = new Data();
        string stored = data.Store.Keep(Encoding.UTF8.GetBytes("flag"), "SOV.png");

        data.Sync("WTO", ModText((Flag, stored)));

        string placed = Path.Combine(data.Mods, "WTO", Flag);
        Assert.Equal("flag", File.ReadAllText(placed));
        Assert.False(File.Exists(Path.Combine(data.Store.Folder, stored)));
        Assert.Equal(placed, data.Store.PathOf(stored));
        Assert.Equal("flag", Encoding.UTF8.GetString(data.Store.Read(stored)!));
    }

    [Fact]
    public void A_file_no_mod_has_goes_back_to_wait_and_an_undo_finds_it()
    {
        using var data = new Data();
        string stored = data.Store.Keep(Encoding.UTF8.GetBytes("flag"), "SOV.png");
        data.Sync("WTO", ModText((Flag, stored)));

        data.Sync("WTO", ModText());

        Assert.False(File.Exists(Path.Combine(data.Mods, "WTO", Flag)));
        Assert.True(File.Exists(Path.Combine(data.Store.Folder, stored)));
        data.Sync("WTO", ModText((Flag, stored)));
        Assert.True(File.Exists(Path.Combine(data.Mods, "WTO", Flag)));
        Assert.False(File.Exists(Path.Combine(data.Store.Folder, stored)));
    }

    [Fact]
    public void A_file_two_mods_use_stays_with_the_other_when_one_drops_it()
    {
        using var data = new Data();
        string stored = data.Store.Keep(Encoding.UTF8.GetBytes("flag"), "SOV.png");
        data.Sync("WTO", ModText((Flag, stored)));
        data.Sync("YSM", ModText((@"GameData\Other.png", stored)));

        data.Sync("WTO", ModText());

        Assert.Equal("flag", File.ReadAllText(Path.Combine(data.Mods, "YSM", @"GameData\Other.png")));
        Assert.False(File.Exists(Path.Combine(data.Store.Folder, stored)));
        Assert.NotNull(data.Store.PathOf(stored));
    }

    [Fact]
    public void A_renamed_mods_folder_follows_and_its_files_are_still_found()
    {
        using var data = new Data();
        string stored = data.Store.Keep(Encoding.UTF8.GetBytes("flag"), "SOV.png");
        data.Sync("WTO", ModText((Flag, stored)));

        data.Views.Rename("WTO", "WTO 2");

        Assert.Equal(Path.Combine(data.Mods, "WTO 2", Flag), data.Store.PathOf(stored));
    }

    [Fact]
    public void A_picture_moves_into_Armory_Pictures_and_is_found_by_its_name_from_any_computer()
    {
        using var data = new Data();
        Directory.CreateDirectory(data.Pictures);
        File.WriteAllText(Path.Combine(data.Pictures, "WTO - Emblem.png"), "emblem");
        string text = new JsonObject { ["EmblemFile"] = @"C:\Users\someone\AppData\Local\EasyNDF\Pictures\WTO - Emblem.png" }.ToJsonString();

        data.Sync("WTO", text);

        string placed = Path.Combine(data.Mods, "WTO", ModFileViews.PicturesFolderName, "WTO - Emblem.png");
        Assert.Equal("emblem", File.ReadAllText(placed));
        Assert.False(File.Exists(Path.Combine(data.Pictures, "WTO - Emblem.png")));
        Assert.Equal(placed, data.Views.FindPicture("WTO - Emblem.png"));
    }

    [Fact]
    public void Destinations_that_leave_the_folder_are_left_out()
    {
        Assert.Null(ModFileViews.SafePlace(@"..\Elsewhere\a.png"));
        Assert.Null(ModFileViews.SafePlace(@"C:\Windows\a.png"));
        Assert.Null(ModFileViews.SafePlace(@"Armory Pictures\a.png"));
        Assert.Null(ModFileViews.SafePlace("WTO.mod.json"));
        Assert.Equal(@"GameData\a.png", ModFileViews.SafePlace("/GameData/a.png"));
    }

    [Fact]
    public void Clean_up_finds_unused_files_and_gone_mods_and_keeps_what_a_mod_still_uses()
    {
        using var data = new Data();
        string used = data.Store.Keep(Encoding.UTF8.GetBytes("used"), "a.png");
        string unused = data.Store.Keep(Encoding.UTF8.GetBytes("unused"), "b.png");
        data.Sync("Gone", ModText((Flag, used)));
        File.Delete(Path.Combine(data.Mods, "Gone", "Gone.mod.json"));
        string live = ModText((Flag, used));

        ModFileViews.CleanUp found = data.Views.FindUnused([("Live", live)]);

        Assert.Equal([Path.Combine(data.Mods, "Gone")], found.Folders);
        Assert.Equal([Path.Combine(data.Store.Folder, unused)], found.Files);
        data.Views.Remove(found);
        Assert.False(Directory.Exists(Path.Combine(data.Mods, "Gone")));
        Assert.NotNull(data.Store.PathOf(used));
        Assert.Null(data.Store.PathOf(unused));
        data.Sync("Live", live);
        Assert.Equal("used", File.ReadAllText(Path.Combine(data.Mods, "Live", Flag)));
    }
}
