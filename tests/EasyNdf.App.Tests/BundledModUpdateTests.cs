using System.Text.Json.Nodes;

namespace EasyNdf.App.Tests;

/// <summary>After an update, the user is asked once about each new bundled mod, unless told not to or the mod is gone.</summary>
public class BundledModUpdateTests
{
    private static bool Due(JsonObject state, JsonObject settings, string text = "new", params string[] mods) =>
        MainView.BundledModUpdateIsDue(state, settings, text, mods, "WTO");

    [Fact]
    public void A_new_bundled_mod_is_offered_once()
    {
        var state = new JsonObject();
        Assert.True(Due(state, [], "new", "WTO", "My mod"));
        Assert.False(Due(state, [], "new", "WTO", "My mod"));
        Assert.True(Due(state, [], "newer", "WTO"));
    }

    [Fact]
    public void Not_asked_when_switched_off_or_the_mod_is_gone_and_still_remembered_as_offered()
    {
        var state = new JsonObject();
        Assert.False(Due(state, new JsonObject { [MainView.AskBundledModSetting] = false }, "new", "WTO"));
        Assert.False(Due(state, [], "new", "WTO"));
        Assert.False(Due([], [], "new", "My mod"));
    }

    [Fact]
    public void The_bundled_mod_is_read_and_has_its_rule_sets()
    {
        EasyNdf.Data.Mod mod = EasyNdf.App.Mods.DefaultMods.TacticalOverhaul;
        Assert.Equal("WARNO Tactical Overhaul", mod.Name);
        Assert.Equal(19, mod.Presets.Count);
        Assert.Contains(mod.Presets, preset => preset.Name == "WTO (TacticalUnits.ndf)");
    }
}
