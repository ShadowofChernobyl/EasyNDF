using EasyNdf.Data.Themes;

namespace EasyNdf.Data.Tests;

public class ThemeTests
{
    [Fact]
    public void Olivine_keeps_its_own_colours_and_bevel()
    {
        var colors = ThemeColors.For(BuiltInThemes.ById["steam"], bevelSetting: null);

        Assert.Equal(BuiltInThemes.Keys.Count, colors.Count);
        Assert.Equal("#3f4a42", colors["hi"]);
        Assert.Equal("#090b0a", colors["lo"]);
        Assert.Equal("#7fb88a", colors["d-add"]); // not in the theme: the success colour
    }

    [Fact]
    public void Turning_bevels_off_gives_one_edge_colour()
    {
        var colors = ThemeColors.For(BuiltInThemes.ById["steam"], bevelSetting: false);
        Assert.Equal(colors["hi"], colors["lo"]);
    }

    [Fact]
    public void A_flat_theme_is_beveled_until_the_setting_is_turned_off()
    {
        // Made from the theme's own colours, as Chromium draws the HTML app's color-mix() edges.
        var light = ThemeColors.For(BuiltInThemes.ById["sollight"], bevelSetting: null);
        Assert.Equal("#f0ede3", light["hi"]);
        Assert.Equal("#898578", light["lo"]);
        var dark = ThemeColors.For(BuiltInThemes.ById["basalt"], bevelSetting: null);
        Assert.Equal("#4c4e52", dark["hi"]);
        Assert.Equal("#0c0d0e", dark["lo"]);
        var flat = ThemeColors.For(BuiltInThemes.ById["basalt"], bevelSetting: false);
        Assert.Equal(flat["hi"], flat["lo"]);
    }

    [Fact]
    public void The_toggle_colour_is_made_from_the_accent_and_the_box_colour()
    {
        // Basalt has no toggle colour: #aeb6c1 and #1d1e21 averaged, halves rounded up.
        Assert.Equal("#666a71", ThemeColors.For(BuiltInThemes.ById["basalt"], null)["toggle"]);
    }

    [Fact]
    public void Light_and_Dark_give_way_to_Paper_and_Slate()
    {
        Assert.Equal("paper", BuiltInThemes.CurrentId("light"));
        Assert.Equal("slate", BuiltInThemes.CurrentId("dark"));
        Assert.Equal("steam", BuiltInThemes.CurrentId("steam"));
        Assert.All(BuiltInThemes.Order, id => Assert.True(BuiltInThemes.ById.ContainsKey(id)));
    }

    [Fact]
    public void Imported_colours_are_checked_and_completed()
    {
        var full = ThemeColors.Complete(new Dictionary<string, string> { ["bg"] = "#ABC", ["ink"] = "red" }, "");

        Assert.Equal("#aabbcc", full["bg"]);
        Assert.Equal("#e3e8e2", full["ink"]); // not a colour: Olivine's
    }
}
