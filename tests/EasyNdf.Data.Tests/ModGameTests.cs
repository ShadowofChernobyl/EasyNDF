using System.Text.Json.Nodes;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Tests;

/// <summary>
/// Each mod is for one game, kept in its file as the game's id; and the game of a file added by hand is told from where
/// it is, when its path says.
/// </summary>
public class ModGameTests
{
    [Fact]
    public void A_mod_keeps_its_game_in_its_file_its_export_and_a_backup()
    {
        var mod = new Mod { Name = "Panzers", Game = GameTitle.SteelDivision2, Presets = [new ModPreset { Name = "Only" }] };

        Assert.Equal("SD2", (string?)JsonNode.Parse(ModFiles.StoredText(mod))!["Game"]);
        Assert.Equal(GameTitle.SteelDivision2, ModFiles.Read(ModFiles.StoredText(mod), "x.mod.json").Single().Game);
        Assert.Equal(GameTitle.SteelDivision2, ModFiles.Read(ModFiles.ExportText(mod, isOn: true), "x.mod.json").Single().Game);
        Assert.Equal(GameTitle.SteelDivision2, ModFiles.Read(ModFiles.BackupText([mod]), "backup.json").Single().Game);
    }

    [Fact]
    public void A_mod_file_without_a_game_is_for_warno()
    {
        // every mod made before mods had a game, and those of the HTML app
        Mod mod = ModFiles.Read("""{ "ModName": "Old", "FormatVersion": 1, "Presets": [] }""", "Old.mod.json").Single();
        Assert.Equal(GameTitle.Warno, mod.Game);
    }

    [Fact]
    public void A_game_this_version_does_not_know_is_taken_for_warno()
    {
        Mod mod = ModFiles.Read("""{ "ModName": "Later", "Game": "SomeGame", "Presets": [] }""", "Later.mod.json").Single();
        Assert.Equal(GameTitle.Warno, mod.Game);
    }

    [Fact]
    public void A_copy_keeps_its_game()
    {
        var mod = new Mod { Name = "Panzers", Game = GameTitle.SteelDivision2 };
        Assert.Equal(GameTitle.SteelDivision2, mod.DeepCopy().Game);
    }

    [Theory]
    [InlineData("WARNO", "WARNO")]
    [InlineData("Steel Division 2", "SD2")]
    [InlineData("SteelDivision2 mods", "SD2")]
    [InlineData("my sd2 stuff", "SD2")]
    [InlineData("warno-mods", "WARNO")]
    public void A_folder_named_for_a_game_names_it(string folder, string game)
    {
        Assert.Equal(game, GameTitle.NamedBy(folder)?.Id);
    }

    [Theory]
    [InlineData("Mods")]
    [InlineData("GameData")]
    // a name that names two games says nothing
    [InlineData("WARNO and SD2")]
    public void A_folder_named_for_no_game_or_for_several_names_none(string folder)
    {
        Assert.Null(GameTitle.NamedBy(folder));
    }

    [Fact]
    public void A_file_in_a_game_folder_is_that_games_even_when_the_folder_names_no_game()
    {
        (GameTitle, string)[] installs = [(GameTitle.SteelDivision2, @"D:\Games\Eugen")];
        Assert.Equal(GameTitle.SteelDivision2, GameTitle.OfPath(@"D:\Games\Eugen\Mods\Mine\GameData\Gameplay\Ammunition.ndf", installs));
        Assert.Equal(GameTitle.SteelDivision2, GameTitle.OfPath(@"D:\Games\Eugen\Mods\ModData\base.zip > GameData/Gameplay/Ammunition.ndf", installs));
    }

    [Fact]
    public void A_file_elsewhere_takes_the_game_of_the_nearest_folder_named_for_one()
    {
        Assert.Equal(GameTitle.Warno, GameTitle.OfPath(@"C:\Work\WARNO\GameData\Ammunition.ndf", []));
        Assert.Equal(GameTitle.SteelDivision2, GameTitle.OfPath(@"C:\Work\WARNO\SD2 port\Ammunition.ndf", []));
        // an archive's name counts, and the folders inside it come first
        Assert.Equal(GameTitle.SteelDivision2, GameTitle.OfPath(@"C:\Downloads\SD2 mod.zip > GameData/Ammunition.ndf", []));
        Assert.Equal(GameTitle.Warno, GameTitle.OfPath(@"C:\Downloads\SD2 mod.zip > WARNO/Ammunition.ndf", []));
    }

    [Fact]
    public void A_file_whose_path_names_no_game_has_none()
    {
        Assert.Null(GameTitle.OfPath(@"C:\Work\GameData\Ammunition.ndf", []));
        Assert.Null(GameTitle.OfPath(@"C:\Downloads\mod.zip > GameData/Ammunition.ndf", []));
    }
}
