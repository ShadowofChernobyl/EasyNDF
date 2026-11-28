using EasyNdf.Data.FileList;

namespace EasyNdf.Data.Tests;

/// <summary>"Has conflicts" shows the files the last run found mods in conflict in, and "Errors or warnings" shows them too.</summary>
public class FileListConflictsTests
{
    private static FileEntry Row(string name, int conflicts = 0, int warnings = 0) =>
        new() { Id = name.Length, Name = name, Folder = "GameData/Gfx", ConflictCount = conflicts, WarningCount = warnings };

    [Fact]
    public void Files_with_conflicts_show_under_Has_conflicts_and_Errors_or_warnings()
    {
        FileEntry conflicted = Row("Ammunition.ndf", conflicts: 3), warned = Row("Decks.ndf", warnings: 1), plain = Row("Weapons.ndf");

        Assert.True(FileListView.PassesShowOnly(conflicted, ShowOnly.HasConflicts));
        Assert.False(FileListView.PassesShowOnly(warned, ShowOnly.HasConflicts));
        Assert.False(FileListView.PassesShowOnly(plain, ShowOnly.HasConflicts));

        Assert.True(FileListView.PassesShowOnly(conflicted, ShowOnly.ErrorsOrWarnings));
        Assert.True(FileListView.PassesShowOnly(warned, ShowOnly.ErrorsOrWarnings));
        Assert.False(FileListView.PassesShowOnly(plain, ShowOnly.ErrorsOrWarnings));
    }
}
