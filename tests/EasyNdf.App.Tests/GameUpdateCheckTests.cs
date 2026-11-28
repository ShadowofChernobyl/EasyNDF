using System.IO;
using System.IO.Compression;
using EasyNdf.App.Files;
using Xunit;

namespace EasyNdf.App.Tests;

/// <summary>An export folder is out of date when it is in a game mod folder whose base.zip is not the game's current one.</summary>
public class GameUpdateCheckTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "easyndf-updatecheck-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static void Zip(string path, string content, DateTime date)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("GameData/a.ndf");
        entry.LastWriteTime = date;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private string Mod(string name, string content, DateTime date)
    {
        string folder = Path.Combine(root, "Game", "Mods", name);
        Zip(Path.Combine(folder, "base.zip"), content, date);
        Directory.CreateDirectory(Path.Combine(folder, "GameData", "Gameplay"));
        return folder;
    }

    private void Game(string content, DateTime date)
    {
        Zip(Path.Combine(root, "Game", "Mods", "ModData", "base.zip"), content, date);
        Directory.CreateDirectory(Path.Combine(root, "Game", "Data", "PC", "201602"));
        Directory.CreateDirectory(Path.Combine(root, "Game", "Data", "PC", "210942"));
    }

    [Fact]
    public void AFolderWithTheGamesOwnBaseFilesIsUpToDate()
    {
        Game("new", new DateTime(2026, 11, 10));
        string mod = Mod("Mine", "new", new DateTime(2026, 11, 10));
        Assert.Null(GameUpdateCheck.Check(mod));
    }

    [Fact]
    public void AFolderWithOlderBaseFilesIsOutOfDateAlsoFromInsideIt()
    {
        Game("new", new DateTime(2026, 11, 10));
        string mod = Mod("Mine", "old", new DateTime(2026, 8, 18));
        File.WriteAllText(Path.Combine(mod, "UpdateMod.bat"), "rem");
        GameUpdateCheck.OutOfDate? found = GameUpdateCheck.Check(Path.Combine(mod, "GameData", "Gameplay"));
        Assert.NotNull(found);
        Assert.Equal(mod, found!.ModFolder);
        Assert.Contains("2026", found.MadeFor);
        Assert.StartsWith("210942", found.Latest);
        Assert.NotNull(found.UpdateScript);
    }

    [Fact]
    public void AnOrdinaryFolderIsNeverOutOfDate()
    {
        Game("new", new DateTime(2026, 11, 10));
        string plain = Path.Combine(root, "Exports");
        Directory.CreateDirectory(plain);
        Assert.Null(GameUpdateCheck.Check(plain));
        Assert.Null(GameUpdateCheck.ModFolderOf(plain));
    }
}
