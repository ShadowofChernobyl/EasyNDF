using EasyNdf.App.Files;
using EasyNdf.Data.Divisions;

namespace EasyNdf.App.Tests;

/// <summary>
/// A dictionary a mod carries (its UNITS.csv) is read by WARNO only from GameData\Localisation\&lt;the mod's folder&gt;:
/// the export puts it there, whatever folder the mod it came from named.
/// </summary>
public sealed class CarriedDictionaryTests
{
    [Fact]
    public void A_carried_dictionary_goes_into_the_mods_own_Localisation_folder()
    {
        var file = new FileAndTextRules.CarriedFile("GameData/Localisation/WARNO ACTUAL/UNITS.csv", "stored");
        Assert.Equal("GameData/Localisation/My Mod/UNITS.csv", FileRunner.InTheModsLocalisation(file, "My Mod").Destination);
    }

    [Fact]
    public void Other_carried_files_stay_where_their_rule_says()
    {
        var picture = new FileAndTextRules.CarriedFile("GameData/Assets/2D/Emblem.png", "stored");
        var csvElsewhere = new FileAndTextRules.CarriedFile("GameData/Gameplay/Localisation.csv", "stored");
        Assert.Equal(picture, FileRunner.InTheModsLocalisation(picture, "My Mod"));
        Assert.Equal(csvElsewhere, FileRunner.InTheModsLocalisation(csvElsewhere, "My Mod"));
    }
}
