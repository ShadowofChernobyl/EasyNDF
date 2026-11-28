using System.Buffers.Binary;
using System.Text;
using EasyNdf.Data.Game;

namespace EasyNdf.Data.Tests;

/// <summary>The game's fonts, found in its archives one file at a time, and kept in the cache.</summary>
public class GameFontsTests
{
    private static readonly byte[] Font = [0, 1, 0, 0, 0, 15, 0, 128, 1, 2, 3];

    /// <summary>One entry of an archive's tree: a file (its offset and size) or a folder (its children after its name).</summary>
    private static byte[] Entry(string name, bool isLast, long offset = 0, long size = 0, byte[]? children = null)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        int headLength = (children is null ? 40 : 8) + nameBytes.Length + nameBytes.Length % 2;
        var entry = new byte[headLength + (children?.Length ?? 0)];
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(0), children is null ? 0 : headLength);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), isLast ? 0 : entry.Length);
        if (children is null)
        {
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8), offset);
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(16), size);
        }
        nameBytes.CopyTo(entry, children is null ? 40 : 8);
        children?.CopyTo(entry, headLength);
        return entry;
    }

    /// <summary>An archive whose tree has a folder, "Assets/Fonts/", with two fonts in it, and a file after it.</summary>
    private static byte[] ArchiveWithFolder()
    {
        byte[] fonts = [.. Entry("Liberator-Medium.ttf", false, 0, Font.Length), .. Entry("Other.ttf", true, Font.Length, 3)];
        byte[] tree = [9, 0, 0, 0, 0, 0, 0, 0, 0, .. Entry("Assets/Fonts/", false, children: fonts), .. Entry("Assets/2D/a.tgv", true, Font.Length + 3, 2)];
        var header = new byte[80];
        "edat"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(25), 80);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(29), (uint)tree.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(33), (uint)(80 + tree.Length));
        return [.. header, .. tree, .. Font, 7, 8, 9, 4, 5];
    }

    [Fact]
    public void A_file_is_found_as_the_whole_list_has_it()
    {
        using var temp = new TemporaryFolder();
        string archive = Path.Combine(temp.Path, "Data.dat");
        File.WriteAllBytes(archive, ArchiveWithFolder());

        List<ArchiveFile> all = EdatArchive.ListFiles(archive, out _, out _);

        Assert.Equal(3, all.Count);
        foreach (ArchiveFile file in all) Assert.Equal(file, EdatArchive.FindFile(archive, file.Name.ToUpperInvariant()) with { Name = file.Name });
        Assert.Null(EdatArchive.FindFile(archive, "Assets/Fonts/Missing.ttf"));
        Assert.Equal(Font, EdatArchive.FindFile(archive, GameFonts.UnitCountFont)!.Read());
    }

    [Fact]
    public void The_games_font_is_copied_into_the_cache_once()
    {
        using var temp = new TemporaryFolder();
        string game = Path.Combine(temp.Path, "WARNO");
        Directory.CreateDirectory(Path.Combine(game, "Data", "PC", "100"));
        File.WriteAllBytes(Path.Combine(game, "Data", "PC", "100", "Data.dat"), ArchiveWithFolder());
        string cache = Path.Combine(temp.Path, "Cache");

        string? copy = GameFonts.UnitCountFontFile(game, cache, GameTitle.Warno, "100");

        Assert.Equal(Font, File.ReadAllBytes(copy!));
        // a game without it: none, remembered until the game is updated
        string other = Path.Combine(temp.Path, "Other");
        Directory.CreateDirectory(Path.Combine(other, "Data", "PC"));
        Assert.Null(GameFonts.UnitCountFontFile(other, cache, GameTitle.SteelDivision2, "5"));
        Assert.True(File.Exists(Path.Combine(cache, "Game fonts", GameTitle.SteelDivision2.Id + " 5", "none.txt")));
    }
}
