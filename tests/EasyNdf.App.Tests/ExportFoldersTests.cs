using System.IO;
using EasyNdf.App.Files;
using EasyNdf.Data.FileList;

namespace EasyNdf.App.Tests;

/// <summary>
/// Where an export puts files added from different folders of one GameData (its Generated folder, and GameData itself
/// for MODDING.csv): each beside the others as in the game's mod, not all in the one folder chosen.
/// </summary>
public sealed class ExportFoldersTests
{
    private static readonly string Mod = Path.Combine(@"C:\", "Mods", "Test Mod", "GameData");

    private static FileEntry Generated(string exportFolder) => new()
    {
        Name = "Divisions.ndf", Folder = "Gameplay/Decks", ExportFolder = exportFolder,
        Source = Path.Combine(Mod, "Generated", "Gameplay", "Decks", "Divisions.ndf"),
    };

    [Fact]
    public void An_export_folder_for_the_Generated_folder_shows_where_GameData_is()
    {
        string export = Path.Combine(@"C:\", "Out", "Generated");
        Assert.Equal(Path.Combine(@"C:\", "Out"), FileRunner.ExportGameDataOf(Generated(export)));
        Assert.Equal(Path.Combine(@"C:\", "Out", "GameData"), FileRunner.ExportGameDataOf(Generated(Path.Combine(@"C:\", "Out", "GameData", "Generated"))));
    }

    [Fact]
    public void A_file_knows_the_folder_it_was_added_from_in_GameData()
    {
        Assert.Equal("Generated", FileRunner.AddedFromInGameData(Generated(@"C:\Out")));
        var dictionary = new FileEntry { Name = "MODDING.csv", Folder = "Localisation", Source = Path.Combine(Mod, "Localisation", "MODDING.csv") };
        Assert.Equal("", FileRunner.AddedFromInGameData(dictionary));
        Assert.Null(FileRunner.AddedFromInGameData(new FileEntry { Name = "A.ndf", Source = @"C:\Desktop\A.ndf" }));
    }
}
