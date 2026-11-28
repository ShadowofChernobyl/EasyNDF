using System.Text.Json.Nodes;
using EasyNdf.Data.Divisions;
using Xunit;

namespace EasyNdf.Data.Tests;

/// <summary>The rules a mod's changes to WARNO's Armory tabs become: the order, the name and tooltip, and the cost rows.</summary>
public class TabChangeRulesTests
{
    private static Division DivisionWith(string name, string matrix, string[] units, string[] deck, params string[] rows) => new()
    {
        Descriptor = "Descriptor_Deck_Division_" + name,
        Name = name,
        CostMatrixName = matrix,
        Units = [.. units.Select(unit => new DivisionUnit { Unit = unit })],
        Costs = rows.ToDictionary(row => row, _ => (IReadOnlyList<int>)[1, 2]),
        Battlegroup = new Battlegroup { Descriptor = "Descriptor_Deck_" + name, Cards = [.. deck.Select(unit => new BattlegroupCard(unit, 1, null))] },
    };

    private static readonly Dictionary<string, string> TabOf = new() { ["Mortar"] = "Support", ["Gun"] = "Support", ["Tank"] = "Tanks" };

    private static JsonArray Rules(Mod mod, params Division[] divisions) =>
        TabChangeRules.For(mod, divisions, TabOf, "Armory Tabs", (what, tab) => what + " " + tab);

    private static List<JsonObject> For(JsonArray rules, string file) =>
        [.. rules.OfType<JsonObject>().Where(rule => (string?)rule["Armory"]?["File"] == file)];

    private static string Value(JsonObject rule) => (string)rule["Actions"]![0]!["Value"]!;

    [Fact]
    public void AModThatChangesNoTabGetsNoRules() => Assert.Empty(Rules(new Mod()));

    [Fact]
    public void ASpareTabIsAddedToTheOrderInAllFiveFiles()
    {
        var mod = new Mod { Tabs = [new ModTab { Factory = "Support", Label = "SUP" }] };
        JsonArray rules = Rules(mod);
        Assert.Equal("[Factory/Logistic, Factory/Infantry, Factory/Art, Factory/Tanks, Factory/Recons, Factory/DCA, Factory/Helis, Factory/Planes, Factory/Support, Factory/Defense]",
            Value(Assert.Single(For(rules, "FactoryResources.ndf"), rule => (string?)rule["Armory"]!["Change"] == "Order")));
        Assert.StartsWith("[Factory/UniversalFactory, Factory/Logistic", Value(Assert.Single(For(rules, "UISpecificShowRoomArmoryComponent.ndf"))));
        Assert.EndsWith("Factory/Planes, Factory/Support]", Value(Assert.Single(For(rules, "UISpecificBattlegroupsListView.ndf"))));
        Assert.Single(For(rules, "UISpecificShowRoomDeckCreatorScreenComponent.ndf"));
        Assert.Single(For(rules, "UISpecificShowRoomGroupsDeckCreatorScreenView.ndf"));
    }

    [Fact]
    public void ASpareTabGetsADescriptionWithItsTexts()
    {
        var mod = new Mod { Tabs = [new ModTab { Factory = "Support", Label = "SUP", TipTitle = "Support", TipText = "Mortars and guns." }] };
        JsonObject rule = Assert.Single(For(Rules(mod), "FactoryResources.ndf"), each => (string?)each["Armory"]!["Change"] == "Tab:Support");
        Assert.Equal("LastIndex: (Factory/Support, TSpecificFactoryDescription(FactoryName = \"EZT_SUPP_N\" FactoryHintTitle = \"EZT_SUPP_T\" FactoryHintBody = \"EZT_SUPP_B\" FactoryHintExtended = \"EZT_SUPP_E\"))", Value(rule));
        var texts = (JsonObject)rule["Armory"]!["Texts"]!;
        Assert.Equal("SUP", (string?)texts["EZT_SUPP_N"]);
        Assert.Equal("Mortars and guns.", (string?)texts["EZT_SUPP_B"]);
        // a text not given takes the one before it, so the game finds every token
        Assert.Equal("Mortars and guns.", (string?)texts["EZT_SUPP_E"]);
        // its texts go into the game's two interface dictionaries, not the units' one
        Assert.False(DivisionExtras.NeedsNamesFile(rule));
        Assert.Equal(["INTERFACE_OUTGAME.csv", "INTERFACE_INGAME.csv"], DivisionExtras.NamesFilesNeeded([rule], EasyNdf.Data.Game.GameTitle.Warno));
        Assert.Contains(DivisionExtras.ArmoryTextsOf([rule], "INTERFACE_INGAME.csv"), text => text.Token == "EZT_SUPP_N" && text.Text == "SUP");
        Assert.Empty(DivisionExtras.ArmoryTextsOf([rule], "UNITS.csv"));
    }

    [Fact]
    public void AGameTabsLabelIsChangedInItsOwnDescription()
    {
        var mod = new Mod { Tabs = [new ModTab { Factory = "Planes", Label = "JET" }] };
        JsonArray rules = Rules(mod);
        JsonObject rule = Assert.Single(For(rules, "FactoryResources.ndf"));
        Assert.Equal("\"AIR\"", (string?)rule["Conditions"]![0]!["Value"]);
        Assert.Equal("TSpecificFactoryDescription", (string?)rule["Params"]!["ScopeType"]);
        Assert.Equal("\"EZT_PLAN_N\"", Value(rule));
        // nothing else is written: the order is the game's
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void EachCostMatrixWhoseDivisionOffersTheTabGetsARowWithASlotForEachPackOfTheReadyMadeDeck()
    {
        var mod = new Mod { Tabs = [new ModTab { Factory = "Support", Slots = [1, 2] }] };
        Division needs = DivisionWith("One", "MatrixCostName_One", ["Mortar", "Tank"], ["Mortar", "Gun", "Mortar", "Tank"], "Tanks");
        Division hasRow = DivisionWith("Two", "MatrixCostName_Two", ["Mortar"], [], "Tanks", "Support");
        Division noUnits = DivisionWith("Three", "MatrixCostName_Three", ["Tank"], [], "Tanks");
        JsonObject rule = Assert.Single(For(Rules(mod, needs, hasRow, noUnits), "DivisionCostMatrix.ndf"));
        Assert.Equal("MatrixCostName_One", (string?)rule["Actions"]![0]!["Target"]);
        // two slots given, three packs of the tab in the deck: the last cost again
        Assert.Equal("LastIndex: (Factory/Support, [1, 2, 2])", Value(rule));
        Assert.Equal("constants", (string?)rule["Params"]!["Scope"]);
    }

    [Fact]
    public void ADivisionsOwnCostRowIsUsedForItAndTheTabsRowForTheOthers()
    {
        var tab = new ModTab { Factory = "Support", Slots = [1, 1], SlotsFor = new() { ["Descriptor_Deck_Division_One"] = [3, 4, 5] } };
        var mod = new Mod { Tabs = [tab] };
        Division one = DivisionWith("One", "MatrixCostName_One", ["Mortar"], []);
        Division two = DivisionWith("Two", "MatrixCostName_Two", ["Mortar"], []);
        List<JsonObject> rows = For(Rules(mod, one, two), "DivisionCostMatrix.ndf");
        Assert.Equal("LastIndex: (Factory/Support, [3, 4, 5])", Value(rows.Single(rule => (string?)rule["Actions"]![0]!["Target"] == "MatrixCostName_One")));
        Assert.Equal("LastIndex: (Factory/Support, [1, 1])", Value(rows.Single(rule => (string?)rule["Actions"]![0]!["Target"] == "MatrixCostName_Two")));
        Assert.Equal([3, 4, 5], ModTab.FromJson(tab.ToJson()).SlotsFor!["Descriptor_Deck_Division_One"]);
    }

    [Fact]
    public void AnOrderOfTheModsOwnIsFollowed()
    {
        var mod = new Mod { TabOrder = ["Tanks", "Infantry"] };
        Assert.StartsWith("[Factory/Tanks, Factory/Infantry, Factory/Logistic", Value(Assert.Single(For(Rules(mod), "FactoryResources.ndf"))));
    }

    [Fact]
    public void TabChangesAreKeptInTheModFile()
    {
        var mod = new Mod { Name = "M", Tabs = [new ModTab { Factory = "Support", Label = "SUP", Slots = [1, 1, 2] }], TabOrder = ["Support", "Tanks"] };
        Mod copy = mod.DeepCopy();
        Assert.Equal("SUP", copy.Tabs[0].Label);
        Assert.Equal([1, 1, 2], copy.Tabs[0].Slots);
        Assert.Equal(["Support", "Tanks"], copy.TabOrder);
    }
}
