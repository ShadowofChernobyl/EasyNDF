using EasyNdf.Data.Game;
using EasyNdf.Data.Units;

namespace EasyNdf.Data.Tests;

public class GameFilesTests
{
    private const string UnitPicture = "GameData:/Assets/2D/Interface/Common/UnitsIcons/NATO/Tank_US.png";

    /// <summary>A made-up game folder: Data\PC with the archives given, by version folder, and an empty base.zip.</summary>
    private static string MakeGame(string root, string name, params (string VersionFolder, (string Name, byte[] Bytes)[] Files)[] archives)
    {
        string game = Path.Combine(root, name);
        foreach (var (version, files) in archives)
        {
            string folder = Path.Combine(game, "Data", "PC", version);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "ZZ_1.dat"), GameFileWriters.Archive(files));
        }
        GameFileWriters.BaseZip(GameInstall.BaseZipOf(game), new Dictionary<string, string>());
        return game;
    }

    [Fact]
    public void Text_keys_are_packed_six_bits_a_character()
    {
        // digits are 1 to 10, letters 11 to 36, the first character highest
        Assert.Equal((ulong)(11 << 6 | 1), Dictionaries.KeyOf("A0"));
        Assert.Equal(Dictionaries.KeyOf("ABC"), Dictionaries.KeyOf("abc"));
        Assert.Null(Dictionaries.KeyOf(""));
        Assert.Null(Dictionaries.KeyOf("TOO_LONG_KEY"));
        Assert.Null(Dictionaries.KeyOf("A-B"));
    }

    [Fact]
    public void A_dictionary_gives_its_texts_by_key()
    {
        byte[] dictionary = GameFileWriters.Dictionary([(Dictionaries.KeyOf("HMNGALJGAU")!.Value, "M1A1 ABRAMS"), (Dictionaries.KeyOf("X1")!.Value, "Ü")]);

        Dictionary<ulong, string> texts = Dictionaries.Read(dictionary);

        Assert.Equal("M1A1 ABRAMS", texts[Dictionaries.KeyOf("HMNGALJGAU")!.Value]);
        Assert.Equal("Ü", texts[Dictionaries.KeyOf("X1")!.Value]);
        Assert.Throws<InvalidDataException>(() => Dictionaries.Read("NOPE1234"u8.ToArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_texture_gives_its_pixels_unpacked_or_packed_with_zlib(bool zlib)
    {
        byte[] pixels = GameFileWriters.Pixels(4, 3, 200, 100, 50);

        Picture picture = Textures.ReadTgv(GameFileWriters.Texture(4, 3, pixels, zlib));

        Assert.Equal((4, 3), (picture.Width, picture.Height));
        Assert.Equal(pixels, picture.Rgba);
    }

    [Fact]
    public void A_block_compressed_texture_block_is_decoded()
    {
        // DXT1: two colours (pure red and pure blue in 5-6-5) and every pixel choosing the first
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0];

        byte[] rgba = BlockTextures.Decode(block, 4, 4, hasAlphaBlock: false);

        Assert.Equal(64, rgba.Length);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[..4]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Part_of_a_block_texture_is_decoded_as_the_same_part_of_the_whole(bool hasAlphaBlock)
    {
        // a 10 by 9 texture (3 by 3 blocks, the last ones cut short) of made-up block bytes
        var random = new Random(7);
        var blocks = new byte[9 * (hasAlphaBlock ? 16 : 8)];
        random.NextBytes(blocks);
        byte[] whole = BlockTextures.Decode(blocks, 10, 9, hasAlphaBlock);

        byte[] part = BlockTextures.DecodePart(blocks, 10, 9, hasAlphaBlock, 3, 2, 6, 7);

        Assert.Equal(new Picture(10, 9, whole).Crop(3, 2, 6, 7).Rgba, part);
    }

    [Fact]
    public void A_picture_is_written_as_a_png_file()
    {
        byte[] png = PngFile.Write(new Picture(2, 2, GameFileWriters.Pixels(2, 2, 1, 2, 3)));

        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Fact]
    public void A_newer_archive_replaces_an_older_ones_file_and_gives_the_version()
    {
        using var temp = new TemporaryFolder();
        string game = MakeGame(temp.Path, "WARNO",
            ("100", [("PC/Texture/A.tgv", [1])]),
            ("100/250", [("PC/Texture/A.tgv", [2, 2]), ("PC/Texture/B.tgv", [3])]));

        GameArchives archives = GameArchives.Open(game);

        Assert.Equal("250", archives.Version);
        Assert.Equal(2, archives.Find("pc/texture/a.tgv")!.Length);
        Assert.Equal([3], archives.Find("PC/Texture/B.tgv")!.Read());
        Assert.Empty(archives.Problems);
    }

    [Fact]
    public void A_broken_archive_is_noted_and_the_rest_still_read()
    {
        using var temp = new TemporaryFolder();
        string game = MakeGame(temp.Path, "WARNO", ("100", [("PC/Texture/A.tgv", [1])]));
        Directory.CreateDirectory(Path.Combine(game, "Data", "PC", "200"));
        File.WriteAllBytes(Path.Combine(game, "Data", "PC", "200", "ZZ_1.dat"), "not an archive at all, but long enough to have a header....................................."u8.ToArray());

        GameArchives archives = GameArchives.Open(game);

        Assert.NotNull(archives.Find("PC/Texture/A.tgv"));
        Assert.Single(archives.Problems);
    }

    [Fact]
    public void English_texts_come_from_the_US_dictionaries_only()
    {
        using var temp = new TemporaryFolder();
        ulong key = Dictionaries.KeyOf("NAMEKEY")!.Value;
        string game = MakeGame(temp.Path, "WARNO", ("100", [
            ("PC/Localisation/US/Localisation/UNITS.dic", GameFileWriters.Dictionary([(key, "Abrams")])),
            ("PC/Localisation/FR/Localisation/UNITS.dic", GameFileWriters.Dictionary([(key, "Abrams (fr)")])),
            ("PC/Localisation/Deprecated/US/UNITS.dic", GameFileWriters.Dictionary([(key, "Old")])),
        ]));

        GameTexts texts = GameTexts.Read(GameArchives.Open(game));

        Assert.Equal("Abrams", texts.Get("NAMEKEY"));
        Assert.Null(texts.Get("OTHERKEY"));
    }

    [Fact]
    public void A_unit_picture_is_read_out_of_the_archives_once_and_saved()
    {
        using var temp = new TemporaryFolder();
        string game = MakeGame(temp.Path, "WARNO", ("100", [
            (GamePictures.OwnTexturePath(UnitPicture), GameFileWriters.Texture(2, 2, GameFileWriters.Pixels(2, 2, 9, 9, 9))),
        ]));
        GameArchives archives = GameArchives.Open(game);
        string cache = Path.Combine(temp.Path, "Cache");
        var store = new UnitPictureStore(cache, GameTitle.Warno, archives.Version, new GamePictures(archives), modFolder: null);

        string? file = store.FileFor(UnitPicture);

        Assert.NotNull(file);
        Assert.Equal(Path.Combine(cache, "Unit pictures", "WARNO 100", "Assets", "2D", "Interface", "Common", "UnitsIcons", "NATO", "Tank_US.png"), file);
        Assert.True(File.Exists(file));
        Assert.Null(store.FileFor("GameData:/Assets/2D/Missing.png"));
    }

    [Fact]
    public void A_mods_own_picture_is_used_where_it_is()
    {
        using var temp = new TemporaryFolder();
        string mod = Path.Combine(temp.Path, "MyMod");
        string own = Path.Combine(mod, "GameData", "Assets", "2D", "Interface", "Common", "UnitsIcons", "NATO", "Tank_US.png");
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllBytes(own, [1, 2, 3]);
        var store = new UnitPictureStore(Path.Combine(temp.Path, "Cache"), GameTitle.Warno, "100", pictures: null, mod);

        Assert.Equal(own, store.FileFor(UnitPicture));
    }

    [Fact]
    public void Pictures_saved_for_an_older_version_of_the_game_are_deleted_once_unused()
    {
        using var temp = new TemporaryFolder();
        string cache = Path.Combine(temp.Path, "Cache");
        string old = Path.Combine(cache, "Unit pictures", "WARNO 90");
        string otherGame = Path.Combine(cache, "Unit pictures", "SD2 90");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(otherGame);
        // unused for longer than they are kept; one used lately may be on screen in another copy of the app
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow - VersionFolders.KeptUnused - TimeSpan.FromDays(1));
        Directory.SetLastWriteTimeUtc(otherGame, DateTime.UtcNow - VersionFolders.KeptUnused - TimeSpan.FromDays(1));

        _ = new UnitPictureStore(cache, GameTitle.Warno, "100", pictures: null, modFolder: null);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(otherGame));
    }

    [Fact]
    public void A_picture_path_outside_the_cache_is_refused()
    {
        using var temp = new TemporaryFolder();
        var store = new UnitPictureStore(Path.Combine(temp.Path, "Cache"), GameTitle.Warno, "100", pictures: null, modFolder: null);

        Assert.Null(store.FileFor("GameData:/../../outside.png"));
    }

    [Fact]
    public void Mods_are_the_folders_with_a_GameData_named_by_their_config()
    {
        using var temp = new TemporaryFolder();
        string game = MakeGame(temp.Path, "WARNO", ("100", [("PC/A.tgv", [1])]));
        string mods = Path.Combine(game, "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "Zulu", "GameData"));
        Directory.CreateDirectory(Path.Combine(mods, "alpha", "GameData"));
        File.WriteAllText(Path.Combine(mods, "alpha", "Config.ini"), "[Properties]\nName = Alpha Mod ; its name\nVersion = 1\n");
        Directory.CreateDirectory(Path.Combine(mods, "NotAMod"));

        List<GameMod> found = GameInstall.ModsOf(game);

        Assert.Equal(["Alpha Mod", "Zulu"], found.Select(mod => mod.Name));
        Assert.True(GameInstall.IsGameFolder(game));
    }

    [Fact]
    public void Games_are_found_in_Steam_and_in_the_libraries_it_lists()
    {
        using var temp = new TemporaryFolder();
        string steam = Path.Combine(temp.Path, "Steam");
        string library = Path.Combine(temp.Path, "Library");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n  \"1\"\n  {\n    \"path\"    \"" + library.Replace("\\", "\\\\") + "\"\n  }\n}\n");
        MakeGame(Path.Combine(library, "steamapps", "common"), "Steel Division 2", ("100", [("PC/A.tgv", [1])]));
        MakeGame(Path.Combine(steam, "steamapps", "common"), "WARNO", ("100", [("PC/A.tgv", [1])]));
        Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", "WARNO", "Mods", "Mine", "GameData"));

        List<UnitSource> sources = UnitSource.FindAll([], [steam]);

        Assert.Equal(["WARNO", "Steel Division 2"], sources.Select(source => source.Name));
        Assert.Equal(GameTitle.SteelDivision2, sources[1].Game);
    }

    [Fact]
    public void A_chosen_game_folder_is_listed_with_its_mods()
    {
        using var temp = new TemporaryFolder();
        string game = MakeGame(temp.Path, "Steel Division 2", ("100", [("PC/A.tgv", [1])]));
        Directory.CreateDirectory(Path.Combine(game, "Mods", "My Mod", "GameData"));

        List<UnitSource> sources = UnitSource.FindAll([game, game], [Path.Combine(temp.Path, "no Steam here")]);

        Assert.Equal(["Steel Division 2", "My Mod"], sources.Select(source => source.Name));
        Assert.All(sources, source => Assert.Equal(GameTitle.SteelDivision2, source.Game));
    }
}
