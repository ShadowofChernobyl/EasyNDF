using EasyNdf.Data.Divisions;

namespace EasyNdf.Data.Tests;

/// <summary>UNITS.csv goes into GameData\Localisation\(the WARNO mod's folder name), whatever folder it was added from.</summary>
public class NamesFilePathTests
{
    private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    [Fact]
    public void The_mod_is_the_folder_under_Mods()
    {
        Assert.Equal("WTO", DivisionExtras.GameModOf(Native("/Games/WARNO/Mods/WTO/GameData")));
        Assert.Equal("WTO", DivisionExtras.GameModOf(Native("/Games/WARNO/Mods/WTO")));
        Assert.Null(DivisionExtras.GameModOf(Native("/Users/Modder/Desktop/Export")));
        Assert.Null(DivisionExtras.GameModOf(Native("/Games/WARNO/Mods")));
    }

    [Fact]
    public void The_folder_under_Localisation_is_the_mods()
    {
        Assert.Equal(Native("/Mods/WTO/GameData/Localisation/WTO/UNITS.csv"),
            DivisionExtras.NamesFilePathFor(Native("/Mods/WTO/GameData/Localisation/Test/UNITS.csv"), "WTO"));
        Assert.Equal(Native("/Out/GameData/Localisation/WTO/UNITS.csv"),
            DivisionExtras.NamesFilePathFor(Native("/Out/GameData/UNITS.csv"), "WTO"));
        Assert.Equal(Native("/Out/UNITS.csv"), DivisionExtras.NamesFilePathFor(Native("/Out/UNITS.csv"), "WTO"));
    }
}
