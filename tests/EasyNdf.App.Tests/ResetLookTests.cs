using System.Text.Json.Nodes;

namespace EasyNdf.App.Tests;

/// <summary>
/// Settings › Reset look and layout: what the app remembers of how it is laid out, and the settings that are its look,
/// go back to their defaults; the mods, the folders and the other settings are kept.
/// </summary>
public sealed class ResetLookTests
{
    [Fact]
    public void The_look_and_layout_go_back_to_default_and_the_users_work_and_other_settings_are_kept()
    {
        AppData data = AppData.InMemory();
        data.State["sizes"] = new JsonObject { ["unitsBeside"] = 1057 };
        data.State["window"] = new JsonObject { ["width"] = 1280 };
        data.State["armoryPart"] = "Battlegroups";
        data.State["windowUnitsTabItem"] = new JsonObject { ["width"] = 1100 };
        data.State["modsOn"] = new JsonArray("YSM");
        data.State["openMod"] = "YSM";
        data.State["exportFolders"] = new JsonObject { ["YSM"] = "C:/Mods/YSM" };
        data.State["dvFilters"] = new JsonObject { ["Tanks"] = "kept" };
        data.Settings["theme"] = "my theme";
        data.Settings["uiScale"] = "125";
        data.Settings["unitsZoomCards"] = 140;
        data.Settings["logCap"] = 99;
        data.Settings["lang"] = "fr";

        data.ResetLook(deleteOwnThemes: false);

        Assert.Null(data.State["sizes"]);
        Assert.Null(data.State["window"]);
        Assert.Null(data.State["windowUnitsTabItem"]);
        Assert.Null(data.State["armoryPart"]);
        Assert.Equal("steam", (string?)data.Settings["theme"]);
        Assert.Equal("100", (string?)data.Settings["uiScale"]);
        Assert.Null(data.Settings["unitsZoomCards"]);

        Assert.Equal("YSM", (string?)data.State["modsOn"]![0]);
        Assert.Equal("YSM", (string?)data.State["openMod"]);
        Assert.Equal("C:/Mods/YSM", (string?)data.State["exportFolders"]!["YSM"]);
        Assert.Equal("kept", (string?)data.State["dvFilters"]!["Tanks"]);
        Assert.Equal(99, (int?)data.Settings["logCap"]);
        Assert.Equal("fr", (string?)data.Settings["lang"]);
    }
}
