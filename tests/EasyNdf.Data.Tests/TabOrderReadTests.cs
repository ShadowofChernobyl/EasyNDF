using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

/// <summary>The Armory's tabs follow the order the armory screen's file lists them in, a modder's own order too.</summary>
public class TabOrderReadTests
{
    private static IReadOnlyList<string> Read(string list)
    {
        using var files = new GameDataFiles([new ChangedFile("GameData/UserInterface/Use/ShowRoom/Views/UISpecificShowRoomArmoryComponent.ndf",
            "ShowRoomArmory is TUISpecificShowRoomArmoryComponent\n(\n    FactoryDisplayOrder =\n    [\n" + list + "\n    ]\n)\n")]);
        return UnitReader.ReadTabOrder(files);
    }

    [Fact]
    public void A_tab_a_modder_puts_second_is_read_second_and_the_all_units_tab_is_left_out()
    {
        Assert.Equal(["Logistic", "Support", "Infantry", "Art"],
            Read("        Factory/UniversalFactory,\n        Factory/Logistic,\n        Factory/Support, // moved here\n        Factory/Infantry,\n        Factory/Art,"));
    }

    [Fact]
    public void Steel_Division_2_names_its_tabs_with_EDefaultFactories()
    {
        Assert.Equal(["Recons", "Infantry", "Tanks"],
            Read("        EDefaultFactories/UniversalFactory,\n        EDefaultFactories/Recons,\n        EDefaultFactories/Infantry,\n        EDefaultFactories/Tanks,"));
    }

    [Fact]
    public void No_list_reads_as_no_order()
    {
        using var files = new GameDataFiles([new ChangedFile("GameData/UserInterface/Use/ShowRoom/Views/Other.ndf", "X is T()")]);
        Assert.Empty(UnitReader.ReadTabOrder(files));
    }
}
