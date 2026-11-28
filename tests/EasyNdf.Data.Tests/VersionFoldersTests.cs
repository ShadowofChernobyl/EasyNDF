using EasyNdf.Data.Game;

namespace EasyNdf.Data.Tests;

/// <summary>The cache's per-version folders: another version's goes only once no app has used it for a while.</summary>
public class VersionFoldersTests
{
    [Fact]
    public void A_version_folder_in_use_elsewhere_is_kept_and_an_unused_one_goes()
    {
        using var temp = new TemporaryFolder();
        string all = Path.Combine(temp.Path, "Unit pictures");
        string current = Path.Combine(all, GameTitle.Warno.Id + " 200");
        string inUse = Path.Combine(all, GameTitle.Warno.Id + " 100");
        string unused = Path.Combine(all, GameTitle.Warno.Id + " 50");
        string otherGame = Path.Combine(all, GameTitle.SteelDivision2.Id + " 10");
        foreach (string folder in new[] { current, inUse, unused, otherGame }) VersionFolders.MarkUsed(folder);
        File.SetLastWriteTimeUtc(Path.Combine(unused, "used.txt"), DateTime.UtcNow - VersionFolders.KeptUnused - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(Path.Combine(otherGame, "used.txt"), DateTime.UtcNow - VersionFolders.KeptUnused - TimeSpan.FromHours(1));

        VersionFolders.DeleteUnused(all, GameTitle.Warno, current);

        Assert.True(Directory.Exists(current));
        // another source of the game, or another copy of the app, used it lately: its pictures may be on screen
        Assert.True(Directory.Exists(inUse));
        Assert.False(Directory.Exists(unused));
        // another game's folders are never touched
        Assert.True(Directory.Exists(otherGame));
    }
}
