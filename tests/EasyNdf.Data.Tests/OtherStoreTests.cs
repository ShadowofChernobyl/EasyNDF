using System.Text.Json.Nodes;

namespace EasyNdf.Data.Tests;

public class OtherStoreTests
{
    [Fact]
    public void Settings_keep_saved_values_over_the_defaults()
    {
        using var folder = new TemporaryFolder();
        var store = new SettingsStore(folder.Path);
        var settings = store.LoadSettings();
        settings["decimals"] = 3;
        store.SaveSettings(settings);

        var loaded = new SettingsStore(folder.Path).LoadSettings();

        Assert.Equal(3, (int)loaded["decimals"]!);
        Assert.Equal("steam", (string?)loaded["theme"]);
    }

    [Fact]
    public void A_theme_file_round_trips_in_the_export_format()
    {
        using var folder = new TemporaryFolder();
        var store = new ThemeStore(folder.Path);
        store.Save(new CustomTheme { Name = "Mine", Scheme = "light", Colors = { ["bg"] = "#ffffff" } });

        CustomTheme theme = new ThemeStore(folder.Path).Themes.Single();

        Assert.Equal("light", theme.Scheme);
        Assert.Equal("#ffffff", theme.Colors["bg"]);
        Assert.Contains("\"EasyNDFTheme\": 1", File.ReadAllText(Directory.GetFiles(folder.Path).Single()));
    }

    [Fact]
    public void Filters_import_with_free_names_and_export_together()
    {
        using var folder = new TemporaryFolder();
        var store = new FilterStore(folder.Path);
        const string file = """{"EasyNDF":"descriptor filters","Version":1,"Filters":{"Long range":{"Gate":"Any","Conditions":[{"On":true,"Name":"","Property":"MaximumRangeGRU","Test":"is greater than","Value":"1000"}]}}}""";

        Assert.Equal(1, store.Import(file, "a.json"));
        Assert.Equal(1, store.Import(file, "a.json"));

        var again = new FilterStore(folder.Path);
        Assert.Equal(["Long range", "Long range (2)"], again.Filters.Select(f => f.Name).Order());
        Assert.Equal("Any", again.Filters[0].Gate);
        Assert.Throws<ModFileException>(() => store.Import("{}", "b.json"));
    }
}

public class ScanStoreTests
{
    [Fact]
    public void Scans_are_saved_as_before_and_read_back()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "scans.json");
        var store = new ScanStore(path);
        store.Scans["Gameplay/Gfx/Ammunition.ndf"] = new ScanInfo(["Damage", "MaxRange"], ["Ammo_A", "Ammo_\"B\""], ["TAmmunitionDescriptor"]);
        store.Scans["Other.ndf"] = new ScanInfo([], ["Ünïcode"], []);

        store.Save();

        // the same text as the whole file built in memory and written, as it was saved before
        var before = new JsonObject();
        foreach ((string key, ScanInfo scan) in store.Scans)
            before[key] = new JsonObject
            {
                ["names"] = new JsonArray([.. scan.Names.Select(n => JsonValue.Create(n))]),
                ["titles"] = new JsonArray([.. scan.Titles.Select(n => JsonValue.Create(n))]),
                ["types"] = new JsonArray([.. scan.Types.Select(n => JsonValue.Create(n))]),
            };
        Assert.Equal(JsonFiles.ToText(before), File.ReadAllText(path));
        var read = new ScanStore(path);
        Assert.Equal(["Damage", "MaxRange"], read.Scans["Gameplay/Gfx/Ammunition.ndf"].Names);
        Assert.Equal(["Ammo_A", "Ammo_\"B\""], read.Scans["Gameplay/Gfx/Ammunition.ndf"].Titles);
        Assert.Equal(["Ünïcode"], read.Scans["Other.ndf"].Titles);
    }

    [Fact]
    public void A_scans_file_that_cannot_be_read_leaves_no_scans()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "scans.json");
        File.WriteAllText(path, "{ \"A.ndf\": { \"names\": [\"Damage\"");

        Assert.Empty(new ScanStore(path).Scans);
    }
}
