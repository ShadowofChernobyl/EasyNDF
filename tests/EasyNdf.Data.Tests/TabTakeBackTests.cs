using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Tests;

/// <summary>Deleting the Armory's tab rules on the Mods tab takes the tab changes back, so the rules are not made again.</summary>
public class TabTakeBackTests
{
    [Fact]
    public void Deleting_every_tab_rule_takes_the_tab_changes_back()
    {
        var mod = new Mod { Name = "m", Game = GameTitle.Warno };
        mod.Tabs.Add(new ModTab { Factory = "Support", Label = "SUP", TipTitle = "SUPPORT", Slots = [1, 1, 2, 3] });
        JsonArray rules = TabChangeRules.For(mod, [], new Dictionary<string, string>(), "Armory Tabs", (what, tab) => what + tab);
        Assert.NotEmpty(rules);

        ArmoryPresets.TakeBack(mod, [], [.. rules]);

        Assert.Empty(mod.Tabs);
        Assert.Null(mod.TabOrder);
        Assert.Empty(TabChangeRules.For(mod, [], new Dictionary<string, string>(), "Armory Tabs", (what, tab) => what + tab));
    }
}
