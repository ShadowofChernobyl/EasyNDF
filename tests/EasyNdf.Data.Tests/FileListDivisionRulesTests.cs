using EasyNdf.Data.FileList;

namespace EasyNdf.Data.Tests;

/// <summary>A file runs rules when a mod that is on has rule sets or Armory changes for it; the Files tab's filter and Status go by that.</summary>
public class FileListDivisionRulesTests
{
    private static FileEntry Row(params ModOnFile[] mods) => new()
    {
        Name = "Divisions.ndf",
        Folder = "GameData/Generated/Gameplay/Decks",
        Source = "loaded",
        On = true,
        Info = "",
        Scanned = true,
        ModsOnFile = mods,
    };

    [Fact]
    public void A_file_with_only_the_Armorys_rules_shows_under_Has_rules_and_is_ready()
    {
        FileEntry row = Row(new ModOnFile("My mod", [], HasArmoryChanges: true));
        Assert.True(FileListView.PassesShowOnly(row, ShowOnly.HasPreset));
        Assert.False(FileListView.PassesShowOnly(row, ShowOnly.NoPreset));
        Assert.Equal(("Ready", StatusKind.Plain), FileStatus.Of(row));
    }

    [Fact]
    public void A_file_no_mod_runs_on_has_no_rules()
    {
        FileEntry row = Row();
        Assert.False(FileListView.PassesShowOnly(row, ShowOnly.HasPreset));
        Assert.True(FileListView.PassesShowOnly(row, ShowOnly.NoPreset));
        Assert.Equal("No rules", FileStatus.Of(row).Text);
    }
}
